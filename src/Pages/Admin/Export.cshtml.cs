using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Admin;

/// <summary>Tenant admins download all of their organisation's data as a ZIP.</summary>
public class ExportModel(AppDbContext db, TenantExportService exporter, StaffAuthService auth, TenantContext tenant, ILogger<ExportModel> logger) : PageModel
{
    [BindProperty] public bool AllTime { get; set; } = true;
    [BindProperty] public DateTime? From { get; set; }
    [BindProperty] public DateTime? To { get; set; }
    [BindProperty] public bool IncludeImages { get; set; }
    [BindProperty] public string? Password { get; set; }

    public List<DataExportLog> History { get; private set; } = new();
    public int ExportsLeftToday { get; private set; }
    public int TotalVisits { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        var userId = StaffAuthService.CurrentUserId(User)!.Value;

        if (ExportsLeftToday <= 0)
            ModelState.AddModelError(string.Empty, $"The limit of {TenantExportService.MaxExportsPerDay} exports per 24 hours has been reached. Try again later.");
        if (!AllTime && (From is null || To is null || From > To))
            ModelState.AddModelError(nameof(From), "Choose a start and end date (start on or before end), or export all time.");
        if (ModelState.IsValid && await auth.ConfirmPasswordAsync(userId, Password) is { } pwError)
            ModelState.AddModelError(nameof(Password), pwError);
        if (!ModelState.IsValid) return Page();

        var request = new ExportRequest(AllTime ? null : From, AllTime ? null : To, IncludeImages);

        // Build into a temporary file (deleted when the download stream closes), so big exports
        // with images don't sit in memory.
        var temp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            var summary = await exporter.WriteZipAsync(request, $"{User.Identity?.Name} <{User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value}>", temp);
            temp.Position = 0;

            db.DataExportLogs.Add(new DataExportLog
            {
                StaffUserId = userId,
                StaffName = User.Identity?.Name ?? "",
                From = request.From,
                To = request.To,
                IncludedImages = request.IncludeImages,
                Visits = summary.Visits,
                Bytes = temp.Length
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Tenant {Tenant}: data export by user {UserId} ({Visits} visits, images {Images}, {Bytes} bytes)",
                tenant.Slug, userId, summary.Visits, request.IncludeImages, temp.Length);

            var range = request.From is null ? "all" : $"{request.From:yyyyMMdd}-{request.To:yyyyMMdd}";
            return File(temp, "application/zip", $"{tenant.Slug}-export-{range}-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }

    private async Task LoadAsync()
    {
        var since = DateTime.Now.AddHours(-24);
        History = await db.DataExportLogs.AsNoTracking().OrderByDescending(l => l.CreatedAt).Take(20).ToListAsync();
        ExportsLeftToday = Math.Max(0, TenantExportService.MaxExportsPerDay - await db.DataExportLogs.CountAsync(l => l.CreatedAt >= since));
        TotalVisits = await db.Visits.CountAsync();
        From ??= DateTime.Today.AddMonths(-3);
        To ??= DateTime.Today;
    }
}
