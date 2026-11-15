using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Admin;

/// <summary>Choose the visitor pass design and what it shows.</summary>
[RequireFeature(Feature.PassTemplates)]
public class PassesModel(SettingsService settings, LookupService lookups) : PageModel
{
    [BindProperty]
    public PassTemplate Template { get; set; }

    [BindProperty] public bool ShowLogo { get; set; }
    [BindProperty] public bool ShowPhoto { get; set; }
    [BindProperty] public bool ShowCompany { get; set; }
    [BindProperty] public bool ShowHost { get; set; }
    [BindProperty] public bool ShowValidUntil { get; set; }

    [BindProperty, StringLength(80), Display(Name = "Footer text")]
    public string? FooterText { get; set; }

    public Branding Brand { get; private set; } = default!;
    public Visit Sample { get; private set; } = default!;

    public async Task OnGetAsync()
    {
        var p = await settings.GetPassSettingsAsync();
        (Template, ShowLogo, ShowPhoto, ShowCompany, ShowHost, ShowValidUntil, FooterText) =
            (p.Template, p.ShowLogo, p.ShowPhoto, p.ShowCompany, p.ShowHost, p.ShowValidUntil, p.FooterText);
        await LoadPreviewAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Enum.IsDefined(Template)) ModelState.AddModelError(nameof(Template), "Choose a template.");
        if (!ModelState.IsValid)
        {
            await LoadPreviewAsync();
            return Page();
        }

        await settings.SavePassSettingsAsync(new PassSettings(Template, ShowLogo, ShowPhoto, ShowCompany, ShowHost, ShowValidUntil, FooterText ?? ""));
        TempData["Message"] = $"Visitor passes will now print with the {PassSettings.Templates.First(t => t.Template == Template).Name} template.";
        return RedirectToPage();
    }

    /// <summary>Settings as currently shown on the form, for drawing the previews.</summary>
    public PassSettings Current(PassTemplate template) =>
        new(template, ShowLogo, ShowPhoto, ShowCompany, ShowHost, ShowValidUntil, FooterText ?? "");

    private async Task LoadPreviewAsync()
    {
        Brand = await settings.GetBrandingAsync();
        var purpose = (await lookups.ActiveNamesAsync(LookupCategory.Purpose)).FirstOrDefault() ?? "Meeting";
        Sample = PassModel.SampleVisit(purpose);
    }
}
