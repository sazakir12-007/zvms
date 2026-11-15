using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Visits;

public class IndexModel(AppDbContext db, BlacklistService blacklist) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Statuses to include (any of them). Empty means all.</summary>
    [BindProperty(SupportsGet = true)] public List<VisitStatus> Statuses { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? Department { get; set; }

    /// <summary>Only visitors on site now and past their expected check-out (ignores the date range).</summary>
    [BindProperty(SupportsGet = true)] public bool Overstaying { get; set; }

    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public List<Visit> Visits { get; private set; } = new();
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public int OverstayingNow { get; private set; }
    public List<string> Departments { get; private set; } = new();
    public HashSet<int> Blacklisted { get; private set; } = [];
    public string DisplayMode { get; private set; } = ViewMode.List;

    /// <summary>Visits per status under the other filters, shown on the status tags.</summary>
    public Dictionary<VisitStatus, int> StatusCounts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        DisplayMode = ViewMode.Resolve(HttpContext, "log", ViewMode.List);
        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;
        Statuses = Statuses.Distinct().ToList();

        StatusCounts = await WithoutStatusFilter()
            .GroupBy(v => v.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var query = Filtered();
        TotalCount = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Visits = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();

        var now = DateTime.Now;
        OverstayingNow = await db.Visits.CountAsync(v => v.Status == VisitStatus.CheckedIn && v.ExpectedCheckOutAt < now);
        Departments = await db.Visits.Where(v => v.Department != null).Select(v => v.Department!).Distinct().OrderBy(d => d).ToListAsync();
        Blacklisted = await blacklist.BlacklistedVisitIdsAsync(Visits);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        From ??= DateTime.Today.AddDays(-30);
        To ??= DateTime.Today;
        var rows = await Filtered().ToListAsync();

        var bytes = Csv.Build(
            ["Badge", "Visitor", "Company", "Email", "Phone", "Host", "Department", "Purpose", "Status",
             "Expected", "Checked in", "Expected check-out", "Checked out", "Overstay (min)", "Agreement accepted"],
            rows.Select(v => new[]
            {
                v.BadgeNumber, v.VisitorName, v.Company, v.Email, v.Phone, v.Employee?.Name, v.Department,
                v.Purpose.ToString(), v.Status.ToString(),
                v.ExpectedAt?.ToString("yyyy-MM-dd HH:mm"),
                v.CheckedInAt?.ToString("yyyy-MM-dd HH:mm"),
                v.ExpectedCheckOutAt?.ToString("yyyy-MM-dd HH:mm"),
                v.CheckedOutAt?.ToString("yyyy-MM-dd HH:mm"),
                v.Overstay is { } o ? ((int)o.TotalMinutes).ToString() : "",
                v.AgreementAccepted ? "Yes" : "No"
            }));
        var name = Overstaying ? $"overstaying-{DateTime.Now:yyyyMMdd-HHmm}" : $"visitors-{From:yyyyMMdd}-{To:yyyyMMdd}";
        return File(bytes, "text/csv", name + ".csv");
    }

    /// <summary>
    /// Query string for this page with the current filters, repeating Statuses once per selected value.
    /// Pass a different status set, page or handler to build links that change just that.
    /// </summary>
    public string QueryFor(IEnumerable<VisitStatus>? statuses = null, int? page = null, string? handler = null, bool? overstaying = null)
    {
        var q = new QueryBuilder();
        if (handler is not null) q.Add("handler", handler);
        if (From is { } f) q.Add(nameof(From), f.ToString("yyyy-MM-dd"));
        if (To is { } t) q.Add(nameof(To), t.ToString("yyyy-MM-dd"));
        foreach (var s in statuses ?? Statuses) q.Add(nameof(Statuses), s.ToString());
        if (!string.IsNullOrWhiteSpace(Department)) q.Add(nameof(Department), Department);
        if (!string.IsNullOrWhiteSpace(Search)) q.Add(nameof(Search), Search);
        if (overstaying ?? Overstaying) q.Add(nameof(Overstaying), "true");
        if (page is > 1) q.Add(nameof(PageNumber), page.Value.ToString());
        return q.ToString();
    }

    /// <summary>Statuses after toggling one on or off, for a tag's link.</summary>
    public IEnumerable<VisitStatus> Toggled(VisitStatus s) =>
        Statuses.Contains(s) ? Statuses.Where(x => x != s) : Statuses.Append(s);

    /// <summary>Visits that were expected, or arrived, within the date range (or overstaying now).</summary>
    private IQueryable<Visit> Filtered()
    {
        var query = WithoutStatusFilter();
        if (!Overstaying && Statuses.Count > 0)
            query = query.Where(v => Statuses.Contains(v.Status));

        return Overstaying
            ? query.OrderBy(v => v.ExpectedCheckOutAt)
            : query.OrderByDescending(v => v.CheckedInAt ?? v.ExpectedAt);
    }

    /// <summary>Every filter except the status tags (used for the tag counts).</summary>
    private IQueryable<Visit> WithoutStatusFilter()
    {
        var query = db.Visits.Include(v => v.Employee).AsQueryable();

        if (Overstaying)
        {
            var now = DateTime.Now;
            query = query.Where(v => v.Status == VisitStatus.CheckedIn && v.ExpectedCheckOutAt < now);
        }
        else
        {
            var from = From!.Value.Date;
            var to = To!.Value.Date.AddDays(1);
            query = query.Where(v => (v.CheckedInAt >= from && v.CheckedInAt < to)
                                  || (v.CheckedInAt == null && v.ExpectedAt >= from && v.ExpectedAt < to));
        }

        if (!string.IsNullOrWhiteSpace(Department))
            query = query.Where(v => v.Department == Department);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = $"%{Search.Trim()}%";
            query = query.Where(v => EF.Functions.Like(v.VisitorName, term)
                                  || EF.Functions.Like(v.Company!, term)
                                  || EF.Functions.Like(v.Email!, term)
                                  || EF.Functions.Like(v.Phone!, term)
                                  || EF.Functions.Like(v.BadgeNumber!, term)
                                  || EF.Functions.Like(v.Employee!.Name, term));
        }

        return query;
    }
}
