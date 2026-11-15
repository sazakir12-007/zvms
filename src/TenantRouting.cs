namespace VisitorManagement;

/// <summary>
/// Tenant pages are reached as /{tenant}/... (the tenant becomes the request's PathBase, see Program.cs).
/// Platform pages (the organisation finder at /, platform admin at /platform) sit outside any tenant.
/// </summary>
public static class TenantPageRouteConvention
{
    public static bool IsPlatformPage(string viewEnginePath) =>
        viewEnginePath.StartsWith("/Platform/", StringComparison.OrdinalIgnoreCase)
        || viewEnginePath.Equals("/Error", StringComparison.OrdinalIgnoreCase);
}
