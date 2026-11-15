using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Admin;

/// <summary>One page to manage every admin-editable dropdown.</summary>
public class ListsModel(LookupService lookups) : PageModel
{
    public record Row(LookupItem Item, int Usage);

    public record Section(LookupCategory Category, string Title, string Description, IReadOnlyList<Row> Rows);

    public List<Section> Sections { get; private set; } = new();

    private static readonly (LookupCategory Category, string Description)[] Layout =
    [
        (LookupCategory.Purpose, "Why someone is visiting. The colour is used on badges, cards and charts."),
        (LookupCategory.Department, "Shown on check-in to narrow the host list, and on the Hosts page."),
        (LookupCategory.StayDuration, "Choices for how long a visitor expects to stay; sets the expected check-out time."),
        (LookupCategory.Building, "Where the visitor will be, chosen at check-in. Used by the evacuation report."),
        (LookupCategory.Floor, "Floor the visitor will be on, chosen at check-in. Used by the evacuation report."),
    ];

    public async Task OnGetAsync()
    {
        foreach (var (category, description) in Layout)
        {
            var rows = new List<Row>();
            foreach (var item in await lookups.AllAsync(category))
                rows.Add(new Row(item, await lookups.UsageAsync(item)));
            Sections.Add(new Section(category, LookupService.DisplayName(category), description, rows));
        }
    }

    public async Task<IActionResult> OnPostAddAsync(LookupCategory category, string? name, string? color, int? minutes)
    {
        var error = await lookups.AddAsync(category, name, color, minutes);
        Report(error, category, error is null ? $"Added to {LookupService.DisplayName(category).ToLowerInvariant()}." : null);
        return Back(category);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, LookupCategory category, string? name, string? color, int? minutes, bool isActive)
    {
        var error = await lookups.UpdateAsync(id, name, color, minutes, isActive);
        Report(error, category, "Saved.");
        return Back(category);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, LookupCategory category, int direction)
    {
        await lookups.MoveAsync(id, direction);
        return Back(category);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, LookupCategory category)
    {
        var error = await lookups.DeleteAsync(id);
        Report(error, category, "Deleted.");
        return Back(category);
    }

    private void Report(string? error, LookupCategory category, string? success)
    {
        if (error is not null) TempData["Error"] = $"{LookupService.DisplayName(category)}: {error}";
        else if (success is not null) TempData["Message"] = $"{LookupService.DisplayName(category)}: {success}";
    }

    private IActionResult Back(LookupCategory category) => Redirect(Url.Page("./Lists") + "#list-" + category);
}
