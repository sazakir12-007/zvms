using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

using VisitorManagement.Models;

namespace VisitorManagement.Pages.Admin;

/// <summary>Company banner shown in the top bar: name, tagline, logo and style.</summary>
[RequireFeature(Feature.CustomBranding)]
public class BrandingModel(SettingsService settings) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Please enter the company name."), StringLength(60), Display(Name = "Company name")]
    public string CompanyName { get; set; } = "";

    [BindProperty, StringLength(80)]
    public string? Tagline { get; set; }

    [BindProperty]
    public BrandDisplay Display { get; set; }

    [BindProperty]
    public BannerStyle Style { get; set; }

    [BindProperty]
    public IFormFile? Logo { get; set; }

    public Branding Current { get; private set; } = default!;

    public async Task OnGetAsync()
    {
        Current = await settings.GetBrandingAsync();
        CompanyName = Current.CompanyName;
        Tagline = Current.Tagline;
        Display = Current.Display;
        Style = Current.Style;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Current = await settings.GetBrandingAsync();

        if (Logo is { Length: > 0 })
        {
            if (Logo.Length > SettingsService.MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(Logo), $"The logo must be at most {SettingsService.MaxLogoBytes / 1024} KB.");
            }
            else
            {
                using var ms = new MemoryStream();
                await Logo.CopyToAsync(ms);
                if (await settings.SaveLogoAsync(ms.ToArray()) is { } error)
                    ModelState.AddModelError(nameof(Logo), error);
            }
        }

        if (!ModelState.IsValid) return Page();

        await settings.SaveBrandingAsync(CompanyName, Tagline, Display, Style);
        TempData["Message"] = "Branding saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveLogoAsync()
    {
        await settings.RemoveLogoAsync();
        TempData["Message"] = "Logo removed. The built-in mark is shown instead.";
        return RedirectToPage();
    }
}
