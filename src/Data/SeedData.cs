using VisitorManagement.Services;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Models;

namespace VisitorManagement.Data;

public static class SeedData
{
    /// <summary>
    /// Applies migrations, then makes sure every tenant has its default dropdown lists.
    /// (New tenants get theirs when created; this backfills lists added in later versions.)
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await PackageService.EnsureDefaultsAsync(db);

        var provisioning = scope.ServiceProvider.GetRequiredService<VisitorManagement.Services.TenantProvisioningService>();
        foreach (var tenant in await db.Tenants.AsNoTracking().ToListAsync())
        {
            await provisioning.AsTenantAsync(tenant, sp =>
            {
                SeedLookups(sp.GetRequiredService<AppDbContext>());
                return Task.FromResult(0);
            });
        }
    }

    /// <summary>Default dropdown options, added once per list (Admin &gt; Lists can change them afterwards).</summary>
    /// <remarks>Runs as the current tenant: the context must have a tenant set.</remarks>
    public static void SeedLookups(AppDbContext db)
    {
        var existing = db.LookupItems.Select(l => l.Category).Distinct().ToHashSet();

        if (!existing.Contains(LookupCategory.Purpose))
        {
            // The colours the badges used before purposes became editable.
            (string Name, string Color)[] purposes =
            [
                ("Meeting", "#0d6efd"), ("Interview", "#6f42c1"), ("Delivery", "#fd7e14"),
                ("Contractor", "#ffc107"), ("Event", "#198754"), ("Other", "#6c757d")
            ];
            // Keep any purpose already used by a visit, even if it isn't one of the defaults.
            var used = db.Visits.Select(v => v.Purpose).Distinct().ToList()
                .Where(p => !string.IsNullOrWhiteSpace(p) && !purposes.Any(x => x.Name == p));
            var all = purposes.Concat(used.Select(p => (p, "#6c757d")));
            db.LookupItems.AddRange(all.Select((p, i) => new LookupItem
            {
                Category = LookupCategory.Purpose, Name = p.Item1, Color = p.Item2, SortOrder = i
            }));
        }

        if (!existing.Contains(LookupCategory.Department))
        {
            var names = db.Employees.Where(e => e.Department != null).Select(e => e.Department!)
                .Union(db.Visits.Where(v => v.Department != null).Select(v => v.Department!))
                .ToList()
                .Select(d => d.Trim()).Where(d => d.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
            db.LookupItems.AddRange(names.Select((d, i) => new LookupItem
            {
                Category = LookupCategory.Department, Name = d, SortOrder = i
            }));
        }

        if (!existing.Contains(LookupCategory.StayDuration))
        {
            int[] minutes = [5, 10, 15, 30, 60, 90, 120, 180, 240, 360, 480];
            db.LookupItems.AddRange(minutes.Select((m, i) => new LookupItem
            {
                Category = LookupCategory.StayDuration, Name = ExpectedStay.Label(m), Minutes = m, SortOrder = i
            }));
        }

        if (!existing.Contains(LookupCategory.Building))
        {
            string[] buildings = ["Building A", "Building B"];
            db.LookupItems.AddRange(buildings.Select((b, i) => new LookupItem { Category = LookupCategory.Building, Name = b, SortOrder = i }));
        }

        if (!existing.Contains(LookupCategory.Floor))
        {
            string[] floors = ["Floor 1", "Floor 2", "Floor 3", "Floor 4"];
            db.LookupItems.AddRange(floors.Select((f, i) => new LookupItem { Category = LookupCategory.Floor, Name = f, SortOrder = i }));
        }

        if (!existing.Contains(LookupCategory.DocumentType))
        {
            string[] types = ["Driver's licence", "Passport", "National ID card", "Company ID", "Other"];
            db.LookupItems.AddRange(types.Select((t, i) => new LookupItem
            {
                Category = LookupCategory.DocumentType, Name = t, SortOrder = i
            }));
        }

        db.SaveChanges();
    }
}
