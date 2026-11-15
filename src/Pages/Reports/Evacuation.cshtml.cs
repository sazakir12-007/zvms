using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Reports;

/// <summary>
/// Evacuation roll call: everyone checked in right now, grouped by building and floor.
/// Safety-critical, so it's available to all staff on every plan (not behind the Reports feature).
/// </summary>
public class EvacuationModel(AppDbContext db, LookupService lookups, BlacklistService blacklist, TenantContext tenant) : PageModel
{
    public const string NotRecorded = "Location not recorded";

    public record FloorGroup(string Floor, List<Visit> Visitors);
    public record BuildingGroup(string Building, List<FloorGroup> Floors)
    {
        public int Count => Floors.Sum(f => f.Visitors.Count);
    }

    [BindProperty(SupportsGet = true)]
    public string? Building { get; set; }

    public List<BuildingGroup> Groups { get; private set; } = new();

    /// <summary>On-site count per building (all buildings, for the filter chips).</summary>
    public List<(string Building, int Count)> Summary { get; private set; } = new();

    public int Total { get; private set; }
    public int Shown => Groups.Sum(g => g.Count);
    public HashSet<int> Blacklisted { get; private set; } = [];
    public DateTime GeneratedAt { get; } = DateTime.Now;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await LoadAsync();
        var rows = Groups.SelectMany(b => b.Floors.SelectMany(f => f.Visitors.Select(v => new[]
        {
            b.Building, f.Floor, v.VisitorName, v.Company, v.Phone, v.Employee?.Name, v.Employee?.Phone,
            v.Purpose, v.CheckedInAt?.ToString("t"), v.ExpectedCheckOutAt?.ToString("t"), v.BadgeNumber, ""
        }))).ToList<string?[]>();
        var bytes = Csv.Build(["Building", "Floor", "Visitor", "Company", "Phone", "Host", "Host phone", "Purpose",
                               "Checked in", "Expected out", "Badge", "Accounted for"], rows);
        return File(bytes, "text/csv", $"evacuation-{tenant.Slug}-{GeneratedAt:yyyyMMdd-HHmm}.csv");
    }

    private async Task LoadAsync()
    {
        var onSite = await db.Visits.AsNoTracking().Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.CheckedIn)
            .OrderBy(v => v.VisitorName)
            .ToListAsync();
        Total = onSite.Count;
        Blacklisted = await blacklist.BlacklistedVisitIdsAsync(onSite);

        // Order buildings and floors as in Admin › Dropdown lists; unknown values after them, "not recorded" last.
        var buildingOrder = (await lookups.AllAsync(LookupCategory.Building)).Select(l => l.Name).ToList();
        var floorOrder = (await lookups.AllAsync(LookupCategory.Floor)).Select(l => l.Name).ToList();
        static int Rank(List<string> order, string name) =>
            name == NotRecorded ? int.MaxValue : order.IndexOf(name) is var i and >= 0 ? i : order.Count;

        static string Key(string? s) => string.IsNullOrWhiteSpace(s) ? NotRecorded : s;

        var byBuilding = onSite.GroupBy(v => Key(v.Building))
            .OrderBy(g => Rank(buildingOrder, g.Key)).ThenBy(g => g.Key)
            .ToList();
        Summary = byBuilding.Select(g => (g.Key, g.Count())).ToList();

        Groups = byBuilding
            .Where(g => string.IsNullOrEmpty(Building) || g.Key == Building)
            .Select(g => new BuildingGroup(g.Key, g
                .GroupBy(v => Key(v.Floor))
                .OrderBy(f => Rank(floorOrder, f.Key)).ThenBy(f => f.Key)
                .Select(f => new FloorGroup(f.Key, f.ToList()))
                .ToList()))
            .ToList();
    }
}
