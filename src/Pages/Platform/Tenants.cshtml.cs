using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Platform admin: list, create and edit tenants.</summary>
public class TenantsModel(AppDbContext db, TenantProvisioningService provisioning, PackageService packages, TenantRegistry registry,
    ILogger<TenantsModel> logger) : PageModel
{
    public class NewTenantInput
    {
        [Required(ErrorMessage = "Choose an ID."), Display(Name = "Organisation ID")]
        public string Slug { get; set; } = "";

        [Required(ErrorMessage = "Enter the name."), StringLength(100), Display(Name = "Organisation name")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Enter the admin's name."), StringLength(60), Display(Name = "Admin name")]
        public string AdminName { get; set; } = "";

        [Required(ErrorMessage = "Enter the admin's email."), EmailAddress, StringLength(200), Display(Name = "Admin email")]
        public string AdminEmail { get; set; } = "";

        [Required(ErrorMessage = "Set a temporary password."), Display(Name = "Temporary password")]
        public string AdminPassword { get; set; } = "";

        [Required(ErrorMessage = "Choose a license package."), Display(Name = "License package")]
        public int? PackageId { get; set; }

        /// <summary>Subscription end; defaults to a trial period.</summary>
        [DataType(DataType.Date), Display(Name = "Valid until")]
        public DateTime? ValidUntil { get; set; } = DateTime.Today.AddDays(Subscription.TrialDays);
    }

    public record Row(Tenant Tenant, int Staff, int Hosts, int Visits, DateTime? LastVisit, PackageService.TenantUsage Usage)
    {
        public LicensePackage? Package => Tenant.LicensePackage;
        public int IncludedLeft => Package is null ? 0 : Math.Max(0, Package.IncludedVisitors - Usage.CheckInsThisPeriod);
        public decimal CostThisPeriod => (Package?.BasePricePerMonth ?? 0) + Usage.CreditsBoughtThisPeriod;
    }

    public List<LicensePackage> Packages { get; private set; } = new();

    /// <summary>Show only organisations on this package (from the packages page).</summary>
    [BindProperty(SupportsGet = true, Name = "package")]
    public int? PackageFilter { get; set; }

    [BindProperty]
    public NewTenantInput New { get; set; } = new();

    public List<Row> Rows { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var (tenant, error) = await provisioning.CreateAsync(New.Slug, New.Name, New.AdminName, New.AdminEmail, New.AdminPassword, New.PackageId!.Value, New.ValidUntil?.Date);
        if (tenant is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            await LoadAsync();
            return Page();
        }

        TempData["Message"] = $"Created {tenant.Name} at /{tenant.Slug}. Give {New.AdminEmail} their temporary password; they'll choose their own at first sign-in.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string slug, string name, bool isActive)
    {
        if (await provisioning.UpdateAsync(id, slug, name, isActive) is { } error) TempData["Error"] = error;
        else TempData["Message"] = "Saved. If you changed the ID, the organisation's old links no longer work.";
        return RedirectToPage();
    }

    /// <summary>Sets the subscription end date, extends it by whole months, or removes the expiry.</summary>
    public async Task<IActionResult> OnPostValidityAsync(int id, DateTime? validUntil, int? extendMonths, bool noExpiry)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();
        var before = tenant.ValidUntil;
        if (noExpiry) tenant.ValidUntil = null;
        else if (extendMonths is > 0 and <= 36) tenant.ValidUntil = Subscription.Extend(tenant.ValidUntil, extendMonths.Value);
        else if (validUntil is { } d) tenant.ValidUntil = d.Date;
        else
        {
            TempData["Error"] = "Choose a date or a number of months.";
            return RedirectToPage(new { package = PackageFilter });
        }
        await db.SaveChangesAsync();
        registry.Forget(tenant.Slug);
        logger.LogInformation("Platform {Admin} changed {Tenant} valid-until {Before:d} -> {After:d}", User.Identity?.Name, tenant.Slug, before, tenant.ValidUntil);
        TempData["Message"] = tenant.ValidUntil is { } u ? $"{tenant.Name} is now valid until {u:d MMM yyyy}." : $"{tenant.Name} no longer expires.";
        return RedirectToPage(new { package = PackageFilter });
    }

    public async Task<IActionResult> OnPostPackageAsync(int id, int packageId)
    {
        if (await packages.AssignAsync(id, packageId) is { } error) TempData["Error"] = error;
        else TempData["Message"] = "License package changed. It applies straight away.";
        return RedirectToPage(new { package = PackageFilter });
    }

    public async Task<IActionResult> OnPostGrantAsync(int id, decimal amount, string? note)
    {
        var by = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? "platform";
        if (await packages.GrantCreditsAsync(id, amount, note, by) is { } error) TempData["Error"] = error;
        else TempData["Message"] = $"Granted {Format.Money(Math.Round(amount, 2))} free credit.";
        return RedirectToPage(new { package = PackageFilter });
    }

    private async Task LoadAsync()
    {
        Packages = await packages.ListAsync();
        var usage = await packages.UsageAsync();
        // Platform view across tenants: counts only, never tenant data itself.
        var staff = await db.StaffUsers.IgnoreQueryFilters().GroupBy(u => u.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var hosts = await db.Employees.IgnoreQueryFilters().GroupBy(e => e.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var visits = await db.Visits.IgnoreQueryFilters().GroupBy(v => v.TenantId)
            .Select(g => new { g.Key, N = g.Count(), Last = g.Max(v => v.CheckedInAt) }).ToDictionaryAsync(x => x.Key);

        Rows = (await db.Tenants.AsNoTracking().Include(t => t.LicensePackage)
                .Where(t => PackageFilter == null || t.LicensePackageId == PackageFilter)
                .OrderBy(t => t.Name).ToListAsync())
            .Select(t => new Row(t, staff.GetValueOrDefault(t.Id), hosts.GetValueOrDefault(t.Id),
                visits.TryGetValue(t.Id, out var v) ? v.N : 0, visits.TryGetValue(t.Id, out var v2) ? v2.Last : null,
                usage.GetValueOrDefault(t.Id) ?? new PackageService.TenantUsage(0, 0, 0)))
            .ToList();
    }
}
