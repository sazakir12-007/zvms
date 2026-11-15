using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>
/// The tenant for the current request, taken from the first URL segment (/acme/...).
/// Scoped: one per request. Everything tenant-specific (database filters, caches, cookies, links) reads it.
/// </summary>
public class TenantContext
{
    public Tenant? Current { get; private set; }

    /// <summary>0 when there's no tenant (platform pages): tenant-owned queries then return nothing.</summary>
    public int Id => Current?.Id ?? 0;

    public string Slug => Current?.Slug ?? "";

    public bool HasTenant => Current is not null;

    public void Set(Tenant tenant) => Current = tenant;

    /// <summary>"/visits/3/photo" -> "/acme/visits/3/photo". For links that can't use asp-page.</summary>
    public string Url(string path) =>
        HasTenant ? "/" + Slug + (path.StartsWith('/') ? path : "/" + path) : path;

    /// <summary>The tenant's home page, e.g. "/acme/".</summary>
    public string Home => HasTenant ? $"/{Slug}/" : "/";
}

/// <summary>Looks up tenants by slug (cached, so every request doesn't hit the database).</summary>
public class TenantRegistry(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<Tenant?> FindBySlugAsync(string slug)
    {
        slug = Tenant.NormalizeSlug(slug);
        return await cache.GetOrCreateAsync("tenant:" + slug, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
        });
    }

    /// <summary>Call after a tenant is created, renamed or (de)activated.</summary>
    public void Forget(string slug) => cache.Remove("tenant:" + Tenant.NormalizeSlug(slug));
}
