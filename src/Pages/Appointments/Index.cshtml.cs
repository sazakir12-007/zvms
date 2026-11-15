using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Appointments;

/// <summary>Upcoming appointments (visits still in the Expected state), grouped by day.</summary>
[RequireFeature(Feature.Appointments)]
public class IndexModel(AppDbContext db, VisitService visits, BlacklistService blacklist) : PageModel
{
    public List<Visit> Upcoming { get; private set; } = new();
    public HashSet<int> Blacklisted { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Upcoming = await db.Visits.Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.Expected)
            .OrderBy(v => v.ExpectedAt)
            .ToListAsync();
        Blacklisted = await blacklist.BlacklistedVisitIdsAsync(Upcoming);
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (await visits.CancelAsync(id)) TempData["Message"] = "Appointment cancelled.";
        return RedirectToPage();
    }
}
