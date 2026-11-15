using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VisitorManagement.Services;

/// <summary>
/// Reads the first sheet of a .xlsx workbook or a .csv file into rows of strings.
/// The .xlsx reader is a small built-in one (no extra packages): it understands shared and inline
/// strings, numbers and booleans, which is all a list of people needs.
/// </summary>
public static class TabularFileReader
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 5000;

    public static List<string[]> Read(Stream stream, string fileName)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (buffer.Length > MaxBytes) throw new InvalidDataException($"The file is larger than {MaxBytes / 1024 / 1024} MB.");
        var bytes = buffer.ToArray();

        // Sniff rather than trust the extension: .xlsx files are zip archives ("PK").
        if (bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B) return ReadXlsx(bytes);
        if (fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Old .xls workbooks aren't supported. In Excel use File › Save As › Excel Workbook (.xlsx) or CSV.");
        return ReadCsv(bytes);
    }

    // ---------- CSV ----------

    private static List<string[]> ReadCsv(byte[] bytes)
    {
        var text = DecodeText(bytes);
        var firstLine = text.Split('\n', 2)[0];
        // Excel in many regions saves "CSV" with semicolons; tab-separated also works.
        var delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();

        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                AddRow(rows, row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); AddRow(rows, row); }
        return rows;
    }

    private static string DecodeText(byte[] bytes)
    {
        // UTF-8 (with or without BOM) is the norm; fall back to Windows-1252 for old Excel CSVs.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // ---------- XLSX ----------

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static List<string[]> ReadXlsx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var shared = ReadSharedStrings(zip);
        var sheetPath = FirstSheetPath(zip) ?? throw new InvalidDataException("The workbook has no worksheets.");
        var sheet = LoadXml(zip.GetEntry(sheetPath) ?? throw new InvalidDataException("The first worksheet couldn't be found."));

        var rows = new List<string[]>();
        foreach (var rowEl in sheet.Descendants(Main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var c in rowEl.Elements(Main + "c"))
                cells[ColumnIndex((string?)c.Attribute("r"), cells.Count == 0 ? 0 : cells.Keys.Max() + 1)] = CellText(c, shared);

            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            AddRow(rows, Enumerable.Range(0, width).Select(i => cells.GetValueOrDefault(i, "")).ToList());
        }
        return rows;
    }

    private static string CellText(XElement c, List<string> shared)
    {
        var type = (string?)c.Attribute("t");
        var value = (string?)c.Element(Main + "v");
        return type switch
        {
            "s" when int.TryParse(value, out var i) && i >= 0 && i < shared.Count => shared[i],
            // Inline strings may be split into rich-text runs; join all their text.
            "inlineStr" => string.Concat(c.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? []),
            "b" => value == "1" ? "TRUE" : "FALSE",
            "str" or "e" => value ?? "",
            // Numbers: show whole numbers without ".0" or exponent (phone numbers are often stored as numbers).
            _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && Math.Abs(d) < 1e15
                => ((long)d).ToString(CultureInfo.InvariantCulture),
            _ => value ?? ""
        };
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        // Each <si> is plain (<t>) or rich text (<r><t>…); phonetic hints (<rPh>) aren't part of the value.
        return LoadXml(entry).Root!.Elements(Main + "si")
            .Select(si => string.Concat(si.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value)))
            .ToList();
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader);
    }

    /// <summary>The first sheet in workbook order (not necessarily sheet1.xml).</summary>
    private static string? FirstSheetPath(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml");
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbook is null || rels is null) return zip.GetEntry("xl/worksheets/sheet1.xml")?.FullName;

        string? relId = null;
        using (var r = XmlReader.Create(workbook.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                {
                    relId = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    break;
                }
            }
        }

        using (var r = XmlReader.Create(rels.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.GetAttribute("Id") == relId)
                {
                    var target = r.GetAttribute("Target") ?? "";
                    return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
            }
        }
        return zip.GetEntry("xl/worksheets/sheet1.xml")?.FullName;
    }

    /// <summary>"C7" -> 2 (zero-based). Falls back to the next position if the cell has no reference.</summary>
    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference)) return fallback;
        var col = 0;
        foreach (var ch in reference)
        {
            if (!char.IsAsciiLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return col == 0 ? fallback : col - 1;
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        if (row.All(string.IsNullOrWhiteSpace)) return; // skip blank lines
        if (rows.Count >= MaxRows) throw new InvalidDataException($"The file has more than {MaxRows} rows. Split it into smaller files.");
        rows.Add(row.Select(c => c.Trim()).ToArray());
    }
}
