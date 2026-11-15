using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Site root: enter your organisation's ID to go to its pages.</summary>
public class IndexModel(TenantRegistry registry) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Org { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Org)) return Page();

        var slug = Tenant.NormalizeSlug(Org);
        var tenant = Tenant.SlugPattern().IsMatch(slug) ? await registry.FindBySlugAsync(slug) : null;
        if (tenant is { IsActive: true }) return Redirect($"/{tenant.Slug}/");

        // Same message whether it doesn't exist or is inactive.
        Error = "We couldn't find an organisation with that ID. Check the ID with your administrator.";
        return Page();
    }
}
