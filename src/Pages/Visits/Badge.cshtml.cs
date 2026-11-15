using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Visits;

public class BadgeModel(AppDbContext db, BlacklistService blacklist, KioskBadgeToken badgeTokens, SettingsService settings, TenantContext tenant) : PageModel
{
    public Visit Visit { get; private set; } = default!;

    public bool HasPhoto { get; private set; }

    /// <summary>Badge printing is disabled for blacklisted visitors.</summary>
    public bool Blacklisted { get; private set; }

    /// <summary>Signature / ID card captured at check-in.</summary>
    public HashSet<AttachmentKind> Attachments { get; private set; } = [];

    /// <summary>True when reached from the self-service kiosk, so we can send the screen back there.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Kiosk { get; set; }

    /// <summary>Kiosk badge token: lets a signed-out visitor see just their own badge for a few minutes.</summary>
    [BindProperty(SupportsGet = true)]
    public string? T { get; set; }

    public bool IsStaff => User.Identity?.IsAuthenticated == true;

    /// <summary>The pass, drawn with the template chosen in Admin › Pass templates.</summary>
    public PassModel Pass { get; private set; } = default!;

    /// <summary>Photo URL, carrying the kiosk token when the viewer isn't signed in.</summary>
    public string PhotoUrl => tenant.Url(IsStaff ? $"/visits/{Visit.Id}/photo" : $"/visits/{Visit.Id}/photo?t={Uri.EscapeDataString(T ?? "")}");

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!IsStaff && !badgeTokens.IsValidFor(T, tenant.Id, id))
        {
            // Expired or someone else's badge: send visitors back to the kiosk, staff to sign in.
            return Kiosk ? RedirectToPage("/CheckIn") : Challenge();
        }

        var visit = await db.Visits.Include(v => v.Employee).FirstOrDefaultAsync(v => v.Id == id);
        if (visit?.CheckedInAt is null) return NotFound();

        Visit = visit;
        HasPhoto = await db.VisitPhotos.AnyAsync(p => p.VisitId == id);
        Blacklisted = await blacklist.FindAsync(visit.Phone) is not null;
        Pass = new PassModel(visit, await settings.GetBrandingAsync(), await settings.GetPassSettingsAsync(),
            HasPhoto ? PhotoUrl : null, Void: Blacklisted);
        Attachments = (await db.VisitAttachments.Where(a => a.VisitId == id).Select(a => a.Kind).ToListAsync()).ToHashSet();
        return Page();
    }
}
