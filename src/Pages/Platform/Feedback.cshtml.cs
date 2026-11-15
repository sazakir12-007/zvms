using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Platform admin: feedback from every organisation, with status and a reply the organisation sees.</summary>
public class FeedbackModel(AppDbContext db, TenantProvisioningService provisioning) : PageModel
{
    public record Row(AppFeedback Feedback, string TenantSlug, string TenantName);

    [BindProperty(SupportsGet = true)] public FeedbackStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public FeedbackCategory? Category { get; set; }
    [BindProperty(SupportsGet = true)] public int? Tenant { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<Row> Rows { get; private set; } = new();
    public List<Tenant> Tenants { get; private set; } = new();
    public Dictionary<FeedbackStatus, int> Counts { get; private set; } = new();
    public double? AverageRating { get; private set; }

    public async Task OnGetAsync()
    {
        // Platform view across tenants: the only cross-tenant read of feedback.
        var all = db.Feedback.IgnoreQueryFilters().AsNoTracking();
        Counts = await all.GroupBy(f => f.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        AverageRating = await all.Where(f => f.Rating != null).AverageAsync(f => (double?)f.Rating);
        Tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync();

        var q = all;
        if (Status is { } s) q = q.Where(f => f.Status == s);
        if (Category is { } c) q = q.Where(f => f.Category == c);
        if (Tenant is { } t) q = q.Where(f => f.TenantId == t);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            q = q.Where(f => f.Subject.Contains(term) || f.Message.Contains(term) || f.SubmittedBy.Contains(term));
        }

        var names = Tenants.ToDictionary(x => x.Id);
        Rows = (await q.OrderByDescending(f => f.CreatedAt).Take(200).ToListAsync())
            .Select(f => new Row(f, names.GetValueOrDefault(f.TenantId)?.Slug ?? "?", names.GetValueOrDefault(f.TenantId)?.Name ?? "(deleted)"))
            .ToList();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, FeedbackStatus status, string? response)
    {
        var owner = await db.Feedback.IgnoreQueryFilters().Where(x => x.Id == id).Select(x => (int?)x.TenantId).FirstOrDefaultAsync();
        var tenant = owner is null ? null : await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == owner);
        if (tenant is null) return NotFound();

        response = string.IsNullOrWhiteSpace(response) ? null : response.Trim();
        if (response is { Length: > 2000 }) response = response[..2000];

        // Saved as the owning organisation: the database refuses cross-tenant writes otherwise.
        await provisioning.AsTenantAsync(tenant, async sp =>
        {
            var tdb = sp.GetRequiredService<AppDbContext>();
            var f = await tdb.Feedback.FirstAsync(x => x.Id == id);
            if (response != f.Response) f.RespondedAt = response is null ? null : DateTime.Now;
            f.Response = response;
            f.Status = status;
            return await tdb.SaveChangesAsync();
        });
        TempData["Message"] = $"Feedback #{id} updated.";
        return RedirectToPage(new { Status, Category, Tenant, Q });
    }
}
