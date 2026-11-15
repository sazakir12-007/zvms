using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Reports;

[RequireFeature(Feature.Reports)]
public class IndexModel(AppDbContext db, ReportService reports, LookupService lookups) : PageModel
{
    private const int MaxDays = 366;

    [BindProperty(SupportsGet = true)] public ReportType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Department { get; set; }
    [BindProperty(SupportsGet = true)] public int? HostId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Purpose { get; set; }

    /// <summary>Quick date ranges: today, week, month, 30d, 90d.</summary>
    [BindProperty(SupportsGet = true)] public string? Range { get; set; }

    public ReportResult? Result { get; private set; }
    public List<string> Departments { get; private set; } = new();
    public List<Employee> Hosts { get; private set; } = new();
    public List<string> Purposes { get; private set; } = new();
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadFiltersAsync();
        if (Type is null) return;

        if (Validate() is { } request)
            Result = await reports.RunAsync(request);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (Type is null || Validate() is not { } request) return RedirectToPage();

        var result = await reports.RunAsync(request);
        var rows = result.Totals is null ? result.Rows : result.Rows.Append(result.Totals);
        var bytes = Csv.Build(result.Columns.Select(c => c.Header), rows);
        var name = $"{result.Info.Title.ToLowerInvariant().Replace(' ', '-')}-{From:yyyyMMdd}-{To:yyyyMMdd}.csv";
        return File(bytes, "text/csv", name);
    }

    private ReportRequest? Validate()
    {
        ApplyRange();
        From ??= DateTime.Today.AddDays(-29);
        To ??= DateTime.Today;

        if (From > To)
        {
            Error = "The start date must be on or before the end date.";
            return null;
        }
        if ((To.Value - From.Value).TotalDays > MaxDays)
        {
            Error = $"Please choose a period of at most {MaxDays} days.";
            return null;
        }
        return new ReportRequest(Type!.Value, From.Value, To.Value, Department, HostId, Purpose);
    }

    private void ApplyRange()
    {
        var today = DateTime.Today;
        (DateTime From, DateTime To)? range = Range switch
        {
            "today" => (today, today),
            "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today),
            "month" => (new DateTime(today.Year, today.Month, 1), today),
            "30d" => (today.AddDays(-29), today),
            "90d" => (today.AddDays(-89), today),
            _ => null
        };
        if (range is { } r) (From, To) = r;
    }

    private async Task LoadFiltersAsync()
    {
        Departments = await db.Visits.Where(v => v.Department != null).Select(v => v.Department!)
            .Union(db.Employees.Where(e => e.Department != null).Select(e => e.Department!))
            .Distinct().OrderBy(d => d).ToListAsync();
        Hosts = await db.Employees.OrderBy(e => e.Name).ToListAsync();
        // Retired purposes stay listed while old visits use them.
        Purposes = (await lookups.AllAsync(LookupCategory.Purpose)).Select(p => p.Name)
            .Union(await db.Visits.Select(v => v.Purpose).Distinct().ToListAsync())
            .ToList();
    }
}
