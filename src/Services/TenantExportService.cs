using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <param name="From">First day of visits to include; null with <paramref name="To"/> null means all visits.</param>
public record ExportRequest(DateTime? From, DateTime? To, bool IncludeImages);

public record ExportSummary(int Visits, int Hosts, int Staff, int Images);

/// <summary>
/// Builds a ZIP of everything one tenant owns (CSV files plus, optionally, images). Runs as the current
/// tenant, so the database filters guarantee nothing from another tenant can end up in the file.
/// </summary>
public class TenantExportService(AppDbContext db, TenantContext tenant)
{
    public const int MaxExportsPerDay = 5;
    private const int ImageBatch = 100;
    private const string Stamp = "yyyy-MM-dd HH:mm:ss";

    public async Task<ExportSummary> WriteZipAsync(ExportRequest request, string exportedBy, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // Visits in range (by arrival, or expected arrival for no-shows).
        var visitQuery = db.Visits.AsNoTracking().Include(v => v.Employee).AsQueryable();
        if (request.From is { } from) visitQuery = visitQuery.Where(v => (v.CheckedInAt ?? v.ExpectedAt) >= from.Date);
        if (request.To is { } to) visitQuery = visitQuery.Where(v => (v.CheckedInAt ?? v.ExpectedAt) < to.Date.AddDays(1));
        var visits = await visitQuery.OrderBy(v => v.Id).ToListAsync();
        var visitIds = visits.Select(v => v.Id).ToList();

        // Which visits have images (ids only, no bytes yet).
        var photoIds = (await ChunkedAsync(visitIds, ids => db.VisitPhotos.Where(p => ids.Contains(p.VisitId)).Select(p => p.VisitId).ToListAsync())).ToHashSet();
        var attachmentKeys = (await ChunkedAsync(visitIds, ids => db.VisitAttachments.Where(a => ids.Contains(a.VisitId))
            .Select(a => new { a.VisitId, a.Kind }).ToListAsync())).Select(a => (a.VisitId, a.Kind)).ToHashSet();

        string? File(bool include, string path) => request.IncludeImages && include ? path : null;
        Add(zip, "visits.csv", Csv.Build(
            ["Id", "Badge", "Visitor", "Company", "Email", "Phone", "Phone verified", "Host", "Host email", "Department", "Purpose", "Status",
             "Expected", "Checked in", "Expected check-out", "Checked out", "Overstay (min)", "Access code", "ID type", "ID number (masked)",
             "Agreement accepted", "Notes", "Created", "Photo file", "Signature file", "ID card file", "Business card file", "Building", "Floor"],
            visits.Select(v => new[]
            {
                v.Id.ToString(), v.BadgeNumber, v.VisitorName, v.Company, v.Email, v.Phone, YesNo(v.PhoneVerified),
                v.Employee?.Name, v.Employee?.Email, v.Department, v.Purpose, v.Status.ToString(),
                v.ExpectedAt?.ToString(Stamp), v.CheckedInAt?.ToString(Stamp), v.ExpectedCheckOutAt?.ToString(Stamp), v.CheckedOutAt?.ToString(Stamp),
                v.Overstay is { } o ? ((int)o.TotalMinutes).ToString() : "", v.AccessCode, v.IdDocumentType, v.IdNumberMasked,
                YesNo(v.AgreementAccepted), v.Notes, v.CreatedAt.ToString(Stamp),
                File(photoIds.Contains(v.Id), $"photos/visit-{v.Id}.jpg"),
                File(attachmentKeys.Contains((v.Id, AttachmentKind.Signature)), $"signatures/visit-{v.Id}.png"),
                File(attachmentKeys.Contains((v.Id, AttachmentKind.IdCard)), $"id-cards/visit-{v.Id}.jpg"),
                File(attachmentKeys.Contains((v.Id, AttachmentKind.BusinessCard)), $"business-cards/visit-{v.Id}.jpg"),
                v.Building, v.Floor
            })));

        var hosts = await db.Employees.AsNoTracking().OrderBy(e => e.Name).ToListAsync();
        Add(zip, "hosts.csv", Csv.Build(["Name", "Email", "Department", "Phone", "Active"],
            hosts.Select(e => new[] { e.Name, e.Email, e.Department, e.Phone, YesNo(e.IsActive) })));

        // Staff accounts without any password data.
        var staff = await db.StaffUsers.AsNoTracking().OrderBy(u => u.Name).ToListAsync();
        Add(zip, "staff.csv", Csv.Build(["Name", "Email", "Role", "Phone", "Active", "Last sign-in", "Created"],
            staff.Select(u => new[] { u.Name, u.Email, u.Role, u.Phone, YesNo(u.IsActive), u.LastSignInAt?.ToString(Stamp), u.CreatedAt.ToString(Stamp) })));

        var blacklist = await db.BlacklistEntries.AsNoTracking().OrderBy(b => b.CreatedAt).ToListAsync();
        Add(zip, "blacklist.csv", Csv.Build(["Name", "Phone", "Reason", "Active", "Added", "Removed", "From visit id"],
            blacklist.Select(b => new[] { b.Name, b.Phone, b.Reason, YesNo(b.IsActive), b.CreatedAt.ToString(Stamp), b.RemovedAt?.ToString(Stamp), b.VisitId?.ToString() })));

        var lists = await db.LookupItems.AsNoTracking().OrderBy(l => l.Category).ThenBy(l => l.SortOrder).ToListAsync();
        Add(zip, "dropdown-lists.csv", Csv.Build(["List", "Name", "Colour", "Minutes", "Order", "Active"],
            lists.Select(l => new[] { l.Category.ToString(), l.Name, l.Color, l.Minutes?.ToString(), l.SortOrder.ToString(), YesNo(l.IsActive) })));

        // Settings; the logo goes in as its own file rather than as base64 text.
        var settings = await db.AppSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
        Add(zip, "settings.csv", Csv.Build(["Key", "Value", "Updated"],
            settings.Where(s => s.Key != "Branding.LogoBase64").Select(s => new[] { s.Key, s.Value, s.UpdatedAt.ToString(Stamp) })));
        var logo = settings.FirstOrDefault(s => s.Key == "Branding.LogoBase64")?.Value;
        var logoType = settings.FirstOrDefault(s => s.Key == "Branding.LogoContentType")?.Value;
        if (!string.IsNullOrEmpty(logo) && logoType is not null)
            Add(zip, "branding/logo" + Extension(logoType), Convert.FromBase64String(logo));

        var images = 0;
        if (request.IncludeImages)
        {
            // In batches, so a large tenant's images are never all in memory at once.
            foreach (var batch in visitIds.Chunk(ImageBatch))
            {
                foreach (var p in await db.VisitPhotos.AsNoTracking().Where(p => batch.Contains(p.VisitId)).ToListAsync())
                {
                    Add(zip, $"photos/visit-{p.VisitId}.jpg", p.Data, CompressionLevel.NoCompression);
                    images++;
                }
                foreach (var a in await db.VisitAttachments.AsNoTracking().Where(a => batch.Contains(a.VisitId)).ToListAsync())
                {
                    var path = a.Kind switch
                    {
                        AttachmentKind.Signature => $"signatures/visit-{a.VisitId}.png",
                        AttachmentKind.BusinessCard => $"business-cards/visit-{a.VisitId}.jpg",
                        _ => $"id-cards/visit-{a.VisitId}.jpg"
                    };
                    Add(zip, path, a.Data, CompressionLevel.NoCompression);
                    images++;
                }
            }
        }

        var summary = new ExportSummary(visits.Count, hosts.Count, staff.Count, images);
        Add(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "vms-tenant-export/1",
            tenant = new { id = tenant.Slug, name = tenant.Current?.Name },
            exportedAt = DateTime.Now.ToString("o"),
            exportedBy,
            visitRange = new { from = request.From?.ToString("yyyy-MM-dd"), to = request.To?.ToString("yyyy-MM-dd") },
            includesImages = request.IncludeImages,
            counts = new { visits = summary.Visits, hosts = summary.Hosts, staff = summary.Staff, blacklist = blacklist.Count, listItems = lists.Count, images }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Add(zip, "README.txt", Encoding.UTF8.GetBytes(Readme(request, summary)));

        return summary;
    }

    private static void Add(ZipArchive zip, string path, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var s = entry.Open();
        s.Write(data);
    }

    // SQLite limits the number of parameters in one IN (...), so look ids up in chunks.
    private static async Task<List<T>> ChunkedAsync<T>(List<int> ids, Func<int[], Task<List<T>>> query)
    {
        var all = new List<T>();
        foreach (var chunk in ids.Chunk(500)) all.AddRange(await query(chunk));
        return all;
    }

    private static string YesNo(bool b) => b ? "Yes" : "No";

    private static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".bin"
    };

    private string Readme(ExportRequest r, ExportSummary s) => $"""
        Visitor Management data export
        ==============================
        Organisation: {tenant.Current?.Name} (/{tenant.Slug})
        Exported:     {DateTime.Now:yyyy-MM-dd HH:mm}
        Visits:       {(r.From is null && r.To is null ? "all" : $"{r.From:yyyy-MM-dd} to {r.To:yyyy-MM-dd}")} ({s.Visits})

        Files
        -----
        visits.csv          One row per visit or appointment. Times are local server time.
        hosts.csv           People visitors come to see.
        staff.csv           Staff accounts (no passwords or password data).
        blacklist.csv       Blacklisted visitors, including removed entries.
        dropdown-lists.csv  Purposes, departments, stay options and ID document types.
        settings.csv        Branding and pass template settings.
        branding/           Company logo, if one was uploaded.
        {(r.IncludeImages ? "photos/, signatures/, business-cards/ (id-cards/ for older visits)   Images named after the visit id in visits.csv." : "(Images were not included in this export.)")}
        manifest.json       Machine-readable summary of this export.

        CSV files are UTF-8 with a byte-order mark, so they open correctly in Excel.

        This file contains personal data about visitors (names, phone numbers{(r.IncludeImages ? ", photos, signatures and ID images" : "")}).
        Store it securely and delete it when it's no longer needed.
        """;
}
