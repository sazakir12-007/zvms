using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Platform admin: license packages (price, visitor allowance, credit, features).</summary>
public class PackagesModel(PackageService packages) : PageModel
{
    public class PackageInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter the name."), StringLength(40)]
        public string Name { get; set; } = "";

        [StringLength(200)]
        public string? Description { get; set; }

        [Range(0, 1_000_000, ErrorMessage = "Enter a price from 0."), Display(Name = "Base price / month")]
        public decimal BasePricePerMonth { get; set; }

        [Range(0, 10_000_000), Display(Name = "Visitors included in base price")]
        public int IncludedVisitors { get; set; }

        [Range(1, 10_000_000), Display(Name = "Max visitors / month")]
        public int MaxVisitorsPerMonth { get; set; }

        [Range(0, 100_000), Display(Name = "Cost per extra visitor")]
        public decimal CostPerExtraVisitor { get; set; }

        [Range(1, 100_000_000), Display(Name = "Minimum credit purchase")]
        public decimal MinCreditPurchase { get; set; }

        public List<Feature> Features { get; set; } = new();

        public bool IsActive { get; set; } = true;

        public static PackageInput From(LicensePackage p) => new()
        {
            Id = p.Id, Name = p.Name, Description = p.Description, BasePricePerMonth = p.BasePricePerMonth,
            IncludedVisitors = p.IncludedVisitors, MaxVisitorsPerMonth = p.MaxVisitorsPerMonth,
            CostPerExtraVisitor = p.CostPerExtraVisitor, MinCreditPurchase = p.MinCreditPurchase,
            Features = [.. p.FeatureSet.OrderBy(f => f)], IsActive = p.IsActive
        };

        public LicensePackage ToPackage() => new()
        {
            Id = Id, Name = Name, Description = Description, BasePricePerMonth = BasePricePerMonth,
            IncludedVisitors = IncludedVisitors, MaxVisitorsPerMonth = MaxVisitorsPerMonth,
            CostPerExtraVisitor = CostPerExtraVisitor, MinCreditPurchase = MinCreditPurchase,
            FeatureSet = [.. Features.Where(Enum.IsDefined)], IsActive = IsActive
        };
    }

    public List<LicensePackage> Packages { get; private set; } = new();
    public Dictionary<int, int> TenantCounts { get; private set; } = new();

    [BindProperty]
    public PackageInput Input { get; set; } = new();

    /// <summary>Package whose edit form is open (0 = new package form) after a failed save.</summary>
    public int? Editing { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (ModelState.IsValid && await packages.SaveAsync(Input.ToPackage()) is { } error)
            ModelState.AddModelError(string.Empty, error);

        if (!ModelState.IsValid)
        {
            Editing = Input.Id;
            await LoadAsync();
            return Page();
        }

        TempData["Message"] = Input.Id == 0
            ? $"Package {Input.Name} created."
            : $"Package {Input.Name} saved. Changes apply to its organisations straight away.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await packages.DeleteAsync(id) is { } error) TempData["Error"] = error;
        else TempData["Message"] = "Package deleted.";
        return RedirectToPage();
    }

    /// <summary>Form values for a package's edit form: what was posted if that form failed, else the saved values.</summary>
    public PackageInput FormFor(LicensePackage? p) =>
        Editing == (p?.Id ?? 0) ? Input : p is null ? NewDefaults() : PackageInput.From(p);

    private static PackageInput NewDefaults() => new()
    {
        IncludedVisitors = 500, MaxVisitorsPerMonth = 2_000, MinCreditPurchase = 500m,
        BasePricePerMonth = 2_499m, CostPerExtraVisitor = 8m
    };

    private async Task LoadAsync()
    {
        Packages = await packages.ListAsync();
        TenantCounts = await packages.TenantCountsAsync();
    }
}
