using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages;

public class IndexModel(AppDbContext db, VisitService visits, BlacklistService blacklist, AnalyticsService analytics, LicenseService license) : PageModel
{
    /// <summary>"overstaying" limits the On site list to visitors past their expected check-out.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    public bool OverstayingOnly => Show == "overstaying";

    public List<Visit> OnSite { get; private set; } = new();
    public int OnSiteTotal { get; private set; }
    public int OverstayingCount { get; private set; }
    public List<Visit> ExpectedToday { get; private set; } = new();
    public int VisitsToday { get; private set; }
    public HashSet<int> Blacklisted { get; private set; } = [];
    public DashboardAnalytics Analytics { get; private set; } = default!;
    public string DisplayMode { get; private set; } = ViewMode.Cards;

    public bool AppointmentsEnabled { get; private set; }
    public bool ChartsEnabled { get; private set; }
    public bool ReportsEnabled { get; private set; }

    public async Task OnGetAsync()
    {
        var plan = await license.GetStatusAsync();
        (AppointmentsEnabled, ChartsEnabled, ReportsEnabled) =
            (plan.Has(Feature.Appointments), plan.Has(Feature.Analytics), plan.Has(Feature.Reports));

        DisplayMode = ViewMode.Resolve(HttpContext, "dashboard", ViewMode.Cards);

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var onSite = await db.Visits.Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.CheckedIn)
            .OrderBy(v => v.CheckedInAt)
            .ToListAsync();
        OnSiteTotal = onSite.Count;
        OverstayingCount = onSite.Count(v => v.IsOverstaying);
        OnSite = OverstayingOnly
            ? onSite.Where(v => v.IsOverstaying).OrderByDescending(v => v.Overstay).ToList()
            : onSite;

        ExpectedToday = await db.Visits.Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.Expected && v.ExpectedAt < tomorrow)
            .OrderBy(v => v.ExpectedAt)
            .ToListAsync();

        VisitsToday = await db.Visits.CountAsync(v => v.CheckedInAt >= today && v.CheckedInAt < tomorrow);
        Blacklisted = await blacklist.BlacklistedVisitIdsAsync(onSite.Concat(ExpectedToday));
        Analytics = await analytics.GetDashboardAsync();
    }

    public async Task<IActionResult> OnPostCheckOutAsync(int id)
    {
        if (await visits.CheckOutAsync(id))
            TempData["Message"] = "Visitor checked out.";
        else
            TempData["Error"] = "That visitor is not checked in.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (await visits.CancelAsync(id)) TempData["Message"] = "Appointment cancelled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckOutAllAsync()
    {
        var count = await visits.CheckOutAllAsync();
        TempData["Message"] = $"Checked out {count} visitor(s).";
        return RedirectToPage();
    }
}
