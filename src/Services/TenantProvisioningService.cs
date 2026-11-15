using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>Creates tenants and runs work "as" a given tenant (each in its own DI scope).</summary>
public class TenantProvisioningService(AppDbContext db, IServiceScopeFactory scopes, TenantRegistry registry, ILogger<TenantProvisioningService> logger)
{
    /// <summary>Runs <paramref name="work"/> with services scoped to <paramref name="tenant"/> (filters, stamping, caches).</summary>
    public async Task<T> AsTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
        return await work(scope.ServiceProvider);
    }

    /// <summary>
    /// Creates a tenant on a license package, with default dropdown lists and its first admin (who must change the temporary
    /// password). Creating the admin here means nobody else can claim a new tenant's setup page.
    /// </summary>
    public async Task<(Tenant? Tenant, string? Error)> CreateAsync(string slug, string name, string adminName, string adminEmail, string temporaryPassword, int packageId, DateTime? validUntil = null)
    {
        slug = Tenant.NormalizeSlug(slug);
        if (Tenant.CheckSlug(slug) is { } slugError) return (null, slugError);
        if (string.IsNullOrWhiteSpace(name)) return (null, "Enter the organisation's name.");
        if (await db.Tenants.AnyAsync(t => t.Slug == slug)) return (null, $"The ID \"{slug}\" is already taken.");
        if (StaffAuthService.CheckPasswordRules(temporaryPassword, adminEmail) is { } pwError) return (null, "Temporary password: " + pwError);

        var package = await db.LicensePackages.FindAsync(packageId);
        if (package is null || !package.IsActive) return (null, "Choose an active license package.");

        var tenant = new Tenant
        {
            Slug = slug, Name = name.Trim(), LicensePackageId = package.Id,
            ValidUntil = validUntil ?? DateTime.Today.AddDays(Subscription.TrialDays)
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var error = await AsTenantAsync(tenant, async sp =>
        {
            var tdb = sp.GetRequiredService<AppDbContext>();
            SeedData.SeedLookups(tdb);
            var (_, err) = await sp.GetRequiredService<StaffAuthService>()
                .CreateAsync(adminName, adminEmail, StaffRoles.Admin, temporaryPassword, mustChangePassword: true);
            return err;
        });

        if (error is not null)
        {
            // Undo: remove the half-created tenant and anything seeded for it.
            await AsTenantAsync(tenant, async sp =>
            {
                var tdb = sp.GetRequiredService<AppDbContext>();
                await tdb.LookupItems.ExecuteDeleteAsync();
                return 0;
            });
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            return (null, "Admin account: " + error);
        }

        registry.Forget(slug);
        logger.LogInformation("Created tenant {Slug} ({Name})", slug, tenant.Name);
        return (tenant, null);
    }

    public async Task<string?> UpdateAsync(int id, string slug, string name, bool isActive)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return "Tenant not found.";

        slug = Tenant.NormalizeSlug(slug);
        if (Tenant.CheckSlug(slug) is { } slugError) return slugError;
        if (string.IsNullOrWhiteSpace(name)) return "Enter the organisation's name.";
        if (await db.Tenants.AnyAsync(t => t.Id != id && t.Slug == slug)) return $"The ID \"{slug}\" is already taken.";

        var oldSlug = tenant.Slug;
        tenant.Slug = slug;
        tenant.Name = name.Trim();
        tenant.IsActive = isActive;
        await db.SaveChangesAsync();
        registry.Forget(oldSlug);
        registry.Forget(slug);
        logger.LogInformation("Updated tenant {Id}: {OldSlug} -> {Slug}, active {Active}", id, oldSlug, slug, isActive);
        return null;
    }
}
