using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Admin;

/// <summary>Choose which fields the check-in and appointment screens show, and which are required.</summary>
public class FieldsModel(SettingsService settings) : PageModel
{
    public FieldSettings Current { get; private set; } = FieldSettings.Defaults;

    public async Task OnGetAsync() => Current = await settings.GetFieldSettingsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var modes = new Dictionary<string, FieldMode>();
        foreach (var f in FieldSettings.All)
        {
            if (Enum.TryParse<FieldMode>(Request.Form[f.Key], out var m) && f.Allowed.Contains(m)) modes[f.Key] = m;
        }
        await settings.SaveFieldSettingsAsync(new FieldSettings(modes));
        TempData["Message"] = "Form fields saved. The check-in and appointment screens use them straight away.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        await settings.SaveFieldSettingsAsync(FieldSettings.Defaults);
        TempData["Message"] = "Form fields reset to the defaults.";
        return RedirectToPage();
    }
}
