using System.Text;

namespace VisitorManagement;

public static class Csv
{
    /// <summary>UTF-8 (with BOM, so Excel reads accents) CSV with every field quoted.</summary>
    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(Field)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(Field)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    // Quote fields and neutralise spreadsheet formula injection.
    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if ("=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
