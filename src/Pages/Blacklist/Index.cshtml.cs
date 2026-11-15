using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Blacklist;

[RequireFeature(Feature.Blacklist)]
public class IndexModel(AppDbContext db, BlacklistService blacklist) : PageModel
{
    public List<BlacklistEntry> Active { get; private set; } = new();
    public List<BlacklistEntry> Removed { get; private set; } = new();

    /// <summary>Visitors on the list who are on site right now (checked in before being blacklisted).</summary>
    public List<Visit> OnSite { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var all = await db.BlacklistEntries.OrderByDescending(b => b.CreatedAt).ToListAsync();
        Active = all.Where(b => b.IsActive).ToList();
        Removed = all.Where(b => !b.IsActive).Take(20).ToList();

        var onSite = await db.Visits.Include(v => v.Employee).Where(v => v.Status == VisitStatus.CheckedIn).ToListAsync();
        OnSite = onSite.Where(v => Active.Any(b => PhoneVerification.SameNumber(b.Phone, v.Phone))).ToList();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        if (await blacklist.RemoveAsync(id)) TempData["Message"] = "Removed from the blacklist.";
        return RedirectToPage();
    }
}
