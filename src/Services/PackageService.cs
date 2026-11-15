using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>Platform admin: license packages, assigning them to organisations, and granting credits.</summary>
public class PackageService(AppDbContext db, TenantRegistry registry, TenantProvisioningService provisioning, ILogger<PackageService> logger)
{
    /// <summary>Usage of one organisation in the current billing period, for the platform's list.</summary>
    public record TenantUsage(int CheckInsThisPeriod, decimal CreditBalance, decimal CreditsBoughtThisPeriod);

    /// <summary>The default Silver / Gold / Platinum packages, created when there are none.</summary>
    public static List<LicensePackage> Defaults() =>
    [
        new()
        {
            Name = "Silver", Description = "For a single reception desk.", SortOrder = 1,
            BasePricePerMonth = 1_499m, IncludedVisitors = 300, MaxVisitorsPerMonth = 1_000,
            CostPerExtraVisitor = 8m, MinCreditPurchase = 500m, FeatureSet = [.. Features.Silver]
        },
        new()
        {
            Name = "Gold", Description = "For busy offices: business card scanning, reports and analytics.", SortOrder = 2,
            BasePricePerMonth = 3_999m, IncludedVisitors = 1_500, MaxVisitorsPerMonth = 5_000,
            CostPerExtraVisitor = 6m, MinCreditPurchase = 1_000m, FeatureSet = [.. Features.Gold]
        },
        new()
        {
            Name = "Platinum", Description = "Everything, with your own branding and pass designs.", SortOrder = 3,
            BasePricePerMonth = 9_999m, IncludedVisitors = 5_000, MaxVisitorsPerMonth = 25_000,
            CostPerExtraVisitor = 4m, MinCreditPurchase = 2_000m, FeatureSet = [.. Features.Platinum]
        },
    ];

    /// <summary>
    /// Seeds the default packages if there are none, and gives organisations without a package
    /// Platinum (so nothing they already use is switched off).
    /// </summary>
    public static async Task EnsureDefaultsAsync(AppDbContext db)
    {
        if (!await db.LicensePackages.AnyAsync())
        {
            db.LicensePackages.AddRange(Defaults());
            await db.SaveChangesAsync();
        }

        var unassigned = await db.Tenants.Where(t => t.LicensePackageId == null).ToListAsync();
        if (unassigned.Count == 0) return;
        var fallback = await db.LicensePackages.Where(p => p.IsActive).OrderByDescending(p => p.SortOrder).FirstOrDefaultAsync()
            ?? await db.LicensePackages.OrderByDescending(p => p.SortOrder).FirstAsync();
        foreach (var t in unassigned) t.LicensePackageId = fallback.Id;
        await db.SaveChangesAsync();
    }

    public Task<List<LicensePackage>> ListAsync(bool activeOnly = false) =>
        db.LicensePackages.AsNoTracking()
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name) // SQLite can't sort decimals
            .ToListAsync();

    /// <summary>Number of organisations on each package.</summary>
    public Task<Dictionary<int, int>> TenantCountsAsync() =>
        db.Tenants.Where(t => t.LicensePackageId != null)
            .GroupBy(t => t.LicensePackageId!.Value)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

    /// <summary>Creates (id 0) or updates a package. Returns an error, or null.</summary>
    public async Task<string?> SaveAsync(LicensePackage input)
    {
        var name = input.Name.Trim();
        if (name.Length == 0) return "Enter the package name.";
        if (await db.LicensePackages.AnyAsync(p => p.Id != input.Id && p.Name == name)) return $"There's already a package called \"{name}\".";
        if (input.IncludedVisitors > input.MaxVisitorsPerMonth) return "Included visitors can't be more than the monthly maximum.";
        if (input.BasePricePerMonth < 0 || input.CostPerExtraVisitor < 0) return "Prices can't be negative.";
        if (input.MinCreditPurchase < 1) return $"The minimum credit purchase must be at least {Format.Money(1)}.";

        LicensePackage package;
        if (input.Id == 0)
        {
            package = new LicensePackage
            {
                SortOrder = (await db.LicensePackages.MaxAsync(p => (int?)p.SortOrder) ?? 0) + 1
            };
            db.LicensePackages.Add(package);
        }
        else
        {
            package = await db.LicensePackages.FindAsync(input.Id) ?? throw new InvalidOperationException("Package not found.");
        }

        package.Name = name;
        package.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        package.BasePricePerMonth = Math.Round(input.BasePricePerMonth, 2);
        package.IncludedVisitors = input.IncludedVisitors;
        package.MaxVisitorsPerMonth = input.MaxVisitorsPerMonth;
        package.CostPerExtraVisitor = Math.Round(input.CostPerExtraVisitor, 4);
        package.MinCreditPurchase = Math.Round(input.MinCreditPurchase, 2);
        package.FeatureList = input.FeatureList;
        package.IsActive = input.IsActive;
        await db.SaveChangesAsync();

        // Tenants cache their record (with the package id); package details are read per request.
        logger.LogInformation("Saved license package {Id} {Name}", package.Id, package.Name);
        return null;
    }

    /// <summary>Deletes a package that no organisation uses. Returns an error, or null.</summary>
    public async Task<string?> DeleteAsync(int id)
    {
        var package = await db.LicensePackages.FindAsync(id);
        if (package is null) return "Package not found.";
        var used = await db.Tenants.CountAsync(t => t.LicensePackageId == id);
        if (used > 0) return $"{package.Name} is used by {used} organisation{(used == 1 ? "" : "s")}. Move them to another package or deactivate it instead.";
        db.LicensePackages.Remove(package);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted license package {Id} {Name}", id, package.Name);
        return null;
    }

    /// <summary>Moves an organisation to another package (takes effect immediately). Returns an error, or null.</summary>
    public async Task<string?> AssignAsync(int tenantId, int packageId)
    {
        var tenant = await db.Tenants.FindAsync(tenantId);
        if (tenant is null) return "Organisation not found.";
        var package = await db.LicensePackages.FindAsync(packageId);
        if (package is null) return "Package not found.";
        if (tenant.LicensePackageId == packageId) return null;
        if (!package.IsActive) return $"{package.Name} is inactive and can't be assigned.";

        tenant.LicensePackageId = packageId;
        await db.SaveChangesAsync();
        registry.Forget(tenant.Slug);
        logger.LogInformation("Tenant {Slug} moved to package {Package}", tenant.Slug, package.Name);
        return null;
    }

    /// <summary>Gives an organisation free credits. Returns an error, or null.</summary>
    /// <param name="amount">Credit to add, as money.</param>
    public async Task<string?> GrantCreditsAsync(int tenantId, decimal amount, string? note, string by)
    {
        amount = Math.Round(amount, 2);
        if (amount is < 1 or > 10_000_000) return $"Enter an amount from {Format.Money(1)} to {Format.Money(10_000_000)}.";
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return "Organisation not found.";

        await provisioning.AsTenantAsync(tenant, async sp =>
        {
            var tdb = sp.GetRequiredService<AppDbContext>();
            tdb.CreditTransactions.Add(new CreditTransaction
            {
                Kind = CreditKind.Grant,
                Amount = amount,
                By = by,
                Note = string.IsNullOrWhiteSpace(note) ? "Granted by the platform" : note.Trim()
            });
            return await tdb.SaveChangesAsync();
        });
        logger.LogInformation("{By} granted {Amount} credit to tenant {Slug}", by, amount, tenant.Slug);
        return null;
    }

    /// <summary>This month's check-ins and credit balance for every organisation (aggregates only).</summary>
    public async Task<Dictionary<int, TenantUsage>> UsageAsync()
    {
        var (start, end) = LicenseService.CurrentPeriod();
        var checkIns = await db.Visits.IgnoreQueryFilters()
            .Where(v => v.CheckedInAt >= start && v.CheckedInAt < end)
            .GroupBy(v => v.TenantId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);
        // SQLite can't sum decimals in SQL; add up in memory.
        var ledger = await db.CreditTransactions.IgnoreQueryFilters()
            .Select(t => new { t.TenantId, t.Kind, t.Amount, t.CreatedAt }).ToListAsync();
        var balances = ledger.GroupBy(t => t.TenantId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var bought = ledger.Where(t => t.Kind == CreditKind.Purchase && t.CreatedAt >= start && t.CreatedAt < end)
            .GroupBy(t => t.TenantId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return checkIns.Keys.Union(balances.Keys).Union(bought.Keys).Distinct().ToDictionary(id => id,
            id => new TenantUsage(checkIns.GetValueOrDefault(id), balances.GetValueOrDefault(id), bought.GetValueOrDefault(id)));
    }
}
