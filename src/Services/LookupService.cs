using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>
/// Admin-managed dropdown options (purposes, departments, stay durations, ID document types).
/// Lists are cached in memory and refreshed whenever an admin changes them.
/// </summary>
public partial class LookupService(AppDbContext db, IMemoryCache cache, TenantContext tenant)
{
    public static string DisplayName(LookupCategory c) => c switch
    {
        LookupCategory.Purpose => "Visit purposes",
        LookupCategory.Department => "Departments",
        LookupCategory.StayDuration => "Expected stay options",
        LookupCategory.DocumentType => "ID document types",
        LookupCategory.Building => "Buildings",
        LookupCategory.Floor => "Floors",
        _ => c.ToString()
    };

    /// <summary>Every option in a list, active or not, in display order.</summary>
    public async Task<IReadOnlyList<LookupItem>> AllAsync(LookupCategory category) =>
        await cache.GetOrCreateAsync(Key(category), async _ =>
            (IReadOnlyList<LookupItem>)await db.LookupItems.AsNoTracking()
                .Where(l => l.Category == category)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
                .ToListAsync()) ?? [];

    public async Task<IReadOnlyList<LookupItem>> ActiveAsync(LookupCategory category) =>
        (await AllAsync(category)).Where(l => l.IsActive).ToList();

    public async Task<List<string>> ActiveNamesAsync(LookupCategory category) =>
        (await ActiveAsync(category)).Select(l => l.Name).ToList();

    /// <summary>Active stay options in minutes, shortest first.</summary>
    public async Task<int[]> StayOptionsAsync() =>
        (await ActiveAsync(LookupCategory.StayDuration)).Where(l => l.Minutes > 0).Select(l => l.Minutes!.Value).Order().ToArray();

    /// <summary>Option names for a dropdown, keeping <paramref name="current"/> listed even if it has been deactivated.</summary>
    public async Task<List<string>> OptionsIncludingAsync(LookupCategory category, string? current)
    {
        var names = await ActiveNamesAsync(category);
        if (!string.IsNullOrWhiteSpace(current) && !names.Contains(current)) names.Add(current);
        return names;
    }

    /// <summary>How many visits / hosts use this option; such options can be deactivated but not deleted.</summary>
    public async Task<int> UsageAsync(LookupItem item) => item.Category switch
    {
        LookupCategory.Purpose => await db.Visits.CountAsync(v => v.Purpose == item.Name),
        LookupCategory.Department => await db.Visits.CountAsync(v => v.Department == item.Name)
                                     + await db.Employees.CountAsync(e => e.Department == item.Name),
        LookupCategory.DocumentType => await db.Visits.CountAsync(v => v.IdDocumentType == item.Name),
        LookupCategory.Building => await db.Visits.CountAsync(v => v.Building == item.Name),
        LookupCategory.Floor => await db.Visits.CountAsync(v => v.Floor == item.Name),
        _ => 0
    };

    public async Task<string?> AddAsync(LookupCategory category, string? name, string? color, int? minutes)
    {
        var item = new LookupItem { Category = category };
        if (Apply(item, name, color, minutes) is { } error) return error;
        if (await NameTakenAsync(item)) return $"\"{item.Name}\" is already in the list.";

        item.SortOrder = (await db.LookupItems.Where(l => l.Category == category).MaxAsync(l => (int?)l.SortOrder) ?? -1) + 1;
        db.LookupItems.Add(item);
        await db.SaveChangesAsync();
        Invalidate(category);
        return null;
    }

    /// <summary>Renames, recolours or (de)activates an option. A rename is carried over to existing visits and hosts.</summary>
    public async Task<string?> UpdateAsync(int id, string? name, string? color, int? minutes, bool isActive)
    {
        var item = await db.LookupItems.FindAsync(id);
        if (item is null) return "That option no longer exists.";

        var oldName = item.Name;
        if (Apply(item, name, color, minutes) is { } error) return error;
        if (await NameTakenAsync(item)) return $"\"{item.Name}\" is already in the list.";
        item.IsActive = isActive;

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        if (oldName != item.Name) await RenameUsagesAsync(item.Category, oldName, item.Name);
        await tx.CommitAsync();

        Invalidate(item.Category);
        return null;
    }

    public async Task MoveAsync(int id, int direction)
    {
        var item = await db.LookupItems.FindAsync(id);
        if (item is null) return;

        var list = await db.LookupItems.Where(l => l.Category == item.Category)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToListAsync();
        var index = list.FindIndex(l => l.Id == id);
        var target = index + Math.Sign(direction);
        if (target < 0 || target >= list.Count) return;

        (list[index], list[target]) = (list[target], list[index]);
        for (var i = 0; i < list.Count; i++) list[i].SortOrder = i; // also repairs any gaps or duplicates
        await db.SaveChangesAsync();
        Invalidate(item.Category);
    }

    public async Task<string?> DeleteAsync(int id)
    {
        var item = await db.LookupItems.FindAsync(id);
        if (item is null) return null;

        var used = await UsageAsync(item);
        if (used > 0) return $"\"{item.Name}\" is used by {used} record(s), so it can't be deleted. Deactivate it instead to hide it from new entries.";

        db.LookupItems.Remove(item);
        await db.SaveChangesAsync();
        Invalidate(item.Category);
        return null;
    }

    /// <summary>CSS giving each purpose its badge colour, used by .purpose-* classes everywhere.</summary>
    public async Task<string> PurposeCssAsync()
    {
        var css = new StringBuilder("/* Generated from Admin > Lists > Visit purposes */\n");
        foreach (var p in await AllAsync(LookupCategory.Purpose))
        {
            var color = IsHexColor(p.Color) ? p.Color! : "#6c757d";
            css.Append('.').Append(Format.PurposeClass(p.Name))
               .Append(" { --purpose-color: ").Append(color)
               .Append("; --purpose-text: ").Append(TextOn(color)).Append("; }\n");
        }
        return css.ToString();
    }

    private string? Apply(LookupItem item, string? name, string? color, int? minutes)
    {
        if (item.Category == LookupCategory.StayDuration)
        {
            if (minutes is not { } m || !ExpectedStay.IsValid(m))
                return $"Enter a stay between {ExpectedStay.MinMinutes} and {ExpectedStay.MaxMinutes} minutes.";
            item.Minutes = m;
            item.Name = ExpectedStay.Label(m);
            return null;
        }

        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return "Please enter a name.";
        if (name.Length > 50) return "Names can be at most 50 characters.";
        item.Name = name;

        if (item.Category == LookupCategory.Purpose)
        {
            if (!IsHexColor(color)) return "Please pick a colour.";
            item.Color = color!.ToLowerInvariant();
        }
        return null;
    }

    private Task<bool> NameTakenAsync(LookupItem item) =>
        db.LookupItems.AnyAsync(l => l.Category == item.Category && l.Id != item.Id && l.Name.ToLower() == item.Name.ToLower());

    private async Task RenameUsagesAsync(LookupCategory category, string from, string to)
    {
        switch (category)
        {
            case LookupCategory.Purpose:
                await db.Visits.Where(v => v.Purpose == from).ExecuteUpdateAsync(s => s.SetProperty(v => v.Purpose, to));
                break;
            case LookupCategory.Department:
                await db.Visits.Where(v => v.Department == from).ExecuteUpdateAsync(s => s.SetProperty(v => v.Department, to));
                await db.Employees.Where(e => e.Department == from).ExecuteUpdateAsync(s => s.SetProperty(e => e.Department, to));
                break;
            case LookupCategory.DocumentType:
                await db.Visits.Where(v => v.IdDocumentType == from).ExecuteUpdateAsync(s => s.SetProperty(v => v.IdDocumentType, to));
                break;
            case LookupCategory.Building:
                await db.Visits.Where(v => v.Building == from).ExecuteUpdateAsync(s => s.SetProperty(v => v.Building, to));
                break;
            case LookupCategory.Floor:
                await db.Visits.Where(v => v.Floor == from).ExecuteUpdateAsync(s => s.SetProperty(v => v.Floor, to));
                break;
        }
    }

    private void Invalidate(LookupCategory category) => cache.Remove(Key(category));

    private string Key(LookupCategory category) => $"lookups:{tenant.Id}:{category}";

    public static bool IsHexColor(string? s) => s is not null && HexColor().IsMatch(s);

    /// <summary>White text if it meets WCAG AA (4.5:1) on the colour, otherwise black.</summary>
    private static string TextOn(string hex)
    {
        double Channel(int i)
        {
            var c = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        var whiteContrast = 1.05 / (luminance + 0.05);
        return whiteContrast >= 4.5 ? "#fff" : "#000";
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
