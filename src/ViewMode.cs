namespace VisitorManagement;

/// <summary>
/// List vs card layout for visit pages. Chosen with <c>?view=list|cards</c> and remembered
/// per page in a cookie, so it survives paging, filtering and post-redirects.
/// </summary>
public static class ViewMode
{
    public const string List = "list";
    public const string Cards = "cards";

    public static string Resolve(HttpContext http, string pageKey, string defaultMode)
    {
        var cookieName = "vms-view-" + pageKey;
        var requested = http.Request.Query["view"].ToString();

        if (requested is List or Cards)
        {
            http.Response.Cookies.Append(cookieName, requested, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });
            return requested;
        }

        var saved = http.Request.Cookies[cookieName];
        return saved is List or Cards ? saved : defaultMode;
    }
}
