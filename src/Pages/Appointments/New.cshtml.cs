using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Appointments;

[RequireFeature(Feature.Appointments)]
public class NewModel(AppDbContext db, VisitService visits, BlacklistService blacklist, LookupService lookups, SettingsService settings) : PageModel
{
    [BindProperty]
    public Visit Visit { get; set; } = new();

    [BindProperty]
    public int StayMinutes { get; set; } = ExpectedStay.DefaultMinutes;

    public List<Employee> Hosts { get; private set; } = new();
    public List<string> Purposes { get; private set; } = new();
    public int[] StayOptions { get; private set; } = [];

    /// <summary>Which optional fields to show (Admin › Form fields).</summary>
    public FieldSettings Fields { get; private set; } = FieldSettings.Defaults;

    public async Task OnGetAsync()
    {
        Fields = await settings.GetFieldSettingsAsync();
        // Default to the next whole hour.
        var now = DateTime.Now;
        Visit.ExpectedAt = now.Date.AddHours(now.Hour + 1);
        await LoadHostsAsync();
        StayMinutes = ExpectedStay.DefaultFrom(StayOptions);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Fields = await settings.GetFieldSettingsAsync();
        ApplyField("appt.phone", () => Visit.Phone, v => Visit.Phone = v, "Visit.Phone", "the visitor's mobile number");
        ApplyField("appt.company", () => Visit.Company, v => Visit.Company = v, "Visit.Company", "the visitor's company");
        ApplyField("appt.email", () => Visit.Email, v => Visit.Email = v, "Visit.Email", "the visitor's email address");
        ApplyField("appt.notes", () => Visit.Notes, v => Visit.Notes = v, "Visit.Notes", "notes for reception");
        if (!Fields.Shown("appt.stay")) StayMinutes = ExpectedStay.DefaultFrom(await lookups.StayOptionsAsync());

        if (Visit.ExpectedAt is null)
            ModelState.AddModelError("Visit.ExpectedAt", "Please choose an expected arrival time.");
        else if (Visit.ExpectedAt < DateTime.Now.AddMinutes(-5))
            ModelState.AddModelError("Visit.ExpectedAt", "Expected arrival must be in the future.");

        if (!(await lookups.ActiveNamesAsync(LookupCategory.Purpose)).Contains(Visit.Purpose))
            ModelState.AddModelError("Visit.Purpose", "Please choose a purpose.");

        if (!ExpectedStay.IsValid(StayMinutes))
            ModelState.AddModelError(nameof(StayMinutes), "Please choose an expected stay.");

        if (!string.IsNullOrWhiteSpace(Visit.Phone) && PhoneVerification.Normalize(Visit.Phone) is null)
            ModelState.AddModelError("Visit.Phone", "Please enter a valid mobile number.");

        // Staff see the reason here; the kiosk would only say "see reception".
        if (await blacklist.FindAsync(Visit.Phone) is { } entry)
            ModelState.AddModelError("Visit.Phone", $"This number is blacklisted ({entry.Name}: {entry.Reason}). The visitor won't be able to check in.");

        if (!ModelState.IsValid)
        {
            await LoadHostsAsync();
            return Page();
        }

        var saved = await visits.CreateAppointmentAsync(Visit, StayMinutes);
        TempData["Message"] = $"Appointment booked for {saved.VisitorName} at {saved.ExpectedAt:g}. Their access code is {saved.AccessCode}.";
        return RedirectToPage("./Index");
    }

    /// <summary>Hidden fields are cleared; required ones must be filled in.</summary>
    private void ApplyField(string key, Func<string?> get, Action<string?> set, string field, string label)
    {
        if (!Fields.Shown(key)) { set(null); ModelState.Remove(field); return; }
        if (Fields.Required(key) && string.IsNullOrWhiteSpace(get()))
            ModelState.AddModelError(field, $"Please enter {label}.");
    }

    private async Task LoadHostsAsync()
    {
        Hosts = await db.Employees.Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync();
        Purposes = await lookups.OptionsIncludingAsync(LookupCategory.Purpose, Visit.Purpose);
        StayOptions = await lookups.StayOptionsAsync();
        if (string.IsNullOrEmpty(Visit.Purpose)) Visit.Purpose = Purposes.FirstOrDefault() ?? "";
    }
}
