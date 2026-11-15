using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages;

public class CheckInModel(
    AppDbContext db,
    VisitService visits,
    BlacklistService blacklist,
    IOtpService otp,
    PhoneVerification phoneVerification,
    LookupService lookups,
    KioskBadgeToken badgeTokens,
    TenantContext tenant,
    LicenseService license,
    SettingsService settings,
    ILogger<CheckInModel> logger) : PageModel
{
    [BindProperty]
    public Visit Visit { get; set; } = new();

    [BindProperty]
    public bool AcceptAgreement { get; set; }

    [BindProperty]
    public string? AccessCode { get; set; }

    /// <summary>JPEG data URL from the webcam widget.</summary>
    [BindProperty]
    public string? PhotoData { get; set; }

    /// <summary>Signed token proving the phone number passed OTP verification.</summary>
    [BindProperty]
    public string? PhoneToken { get; set; }

    /// <summary>How long the visitor expects to stay; sets the expected check-out time.</summary>
    [BindProperty]
    public int StayMinutes { get; set; } = ExpectedStay.DefaultMinutes;

    /// <summary>PNG data URL from the signature pad.</summary>
    [BindProperty]
    public string? SignatureData { get; set; }

    /// <summary>Optional JPEG data URL of the visitor's business card (walk-ins), kept with the visit.</summary>
    [BindProperty]
    public string? BusinessCardImageData { get; set; }

    public List<string> Purposes { get; private set; } = new();
    public int[] StayOptions { get; private set; } = [];
    public List<string> Buildings { get; private set; } = new();
    public List<string> Floors { get; private set; } = new();

    /// <summary>Where the visitor will be; required when the admin has set up buildings / floors.</summary>
    [BindProperty]
    public string? Building { get; set; }

    [BindProperty]
    public string? Floor { get; set; }

    /// <summary>Appointment found by access code or phone, awaiting confirmation.</summary>
    public Visit? Found { get; private set; }

    public List<Employee> Hosts { get; private set; } = new();

    public bool IsDummyOtp => otp is DummyOtpService;

    /// <summary>Which lookup tab is open: "code" or "phone".</summary>
    public string LookupBy { get; private set; } = "code";

    /// <summary>Set when a verified number has no booking, so the walk-in form can say so.</summary>
    public bool PhoneVerifiedWithoutBooking { get; private set; }

    /// <summary>The visitor is blacklisted; the page shows a neutral "see reception" message.</summary>
    public bool Blocked { get; private set; }

    /// <summary>Plan features shown on this page.</summary>
    public bool AppointmentsEnabled { get; private set; }
    public bool CardScanEnabled { get; private set; }

    /// <summary>The plan's check-in allowance is used up. Staff see why; the kiosk just says "see reception".</summary>
    public string? LimitMessage { get; private set; }

    public bool IsStaff => User.Identity?.IsAuthenticated == true;

    /// <summary>Which optional fields to show (Admin › Form fields).</summary>
    public FieldSettings Fields { get; private set; } = FieldSettings.Defaults;

    public override async Task OnPageHandlerExecutionAsync(
        Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutionDelegate next)
    {
        AppointmentsEnabled = await license.HasFeatureAsync(Feature.Appointments);
        Fields = await settings.GetFieldSettingsAsync();
        CardScanEnabled = await license.HasFeatureAsync(Feature.BusinessCardScan) && Fields.Shown("checkin.bcard");
        await next();
    }

    /// <param name="code">Access code to look up straight away (used by the dashboard's Check in button).</param>
    public async Task<IActionResult> OnGetAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || !AppointmentsEnabled)
        {
            await LoadListsAsync();
            return Page();
        }

        AccessCode = code;
        return await OnPostLookupAsync();
    }

    // Step 1 for appointments: look up the access code.
    public async Task<IActionResult> OnPostLookupAsync()
    {
        if (!AppointmentsEnabled) return RedirectToPage();
        ModelState.Clear();
        if (!string.IsNullOrWhiteSpace(AccessCode))
            Found = await visits.FindExpectedByCodeAsync(AccessCode);

        if (Found is null)
            ModelState.AddModelError(nameof(AccessCode), "We couldn't find an appointment with that code.");
        else
            PrefillFrom(Found);

        await LoadListsAsync();
        return Page();
    }

    // Step 1 (alternative): find the appointment by mobile number. The number must pass the OTP first,
    // so typing someone else's number reveals nothing about their visit.
    public async Task<IActionResult> OnPostLookupPhoneAsync()
    {
        if (!AppointmentsEnabled) return RedirectToPage();
        ModelState.Clear();
        LookupBy = "phone";

        var phone = PhoneVerification.Normalize(Visit.Phone);
        if (phone is null || !phoneVerification.IsValid(PhoneToken, phone))
        {
            ModelState.AddModelError("LookupPhone", "Please verify your mobile number with the code we send you.");
        }
        else if (await blacklist.FindAsync(phone) is { } entry)
        {
            return await BlockAsync(entry);
        }
        else
        {
            Found = await visits.FindExpectedByPhoneAsync(phone);
            if (Found is null)
            {
                // Keep the verified number and token so the walk-in form below doesn't ask again.
                PhoneVerifiedWithoutBooking = true;
                ModelState.AddModelError("LookupPhone",
                    "We couldn't find an appointment for this number. Please check in as a walk-in below; your number is already verified.");
            }
            else
            {
                StayMinutes = VisitService.PlannedStayMinutes(Found) ?? StayMinutes;
            }
        }

        await LoadListsAsync();
        return Page();
    }

    // Step 2 for appointments: photo, verified phone, expected stay, agreement, then check in.
    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        if (!AppointmentsEnabled) return RedirectToPage();
        ModelState.Clear();
        if (!Fields.Shown("checkin.stay")) Found = await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id); // keep its planned stay
        var identity = ValidateIdentity();
        ValidateStayAndAgreement();

        if (identity is not null && await blacklist.FindAsync(identity.Value.Phone) is { } entry)
            return await BlockAsync(entry);

        var capture = await ValidateLocationAsync(ValidateCapture(identity?.Photo, identity is not null));

        if (!ModelState.IsValid)
        {
            Found = await db.Visits.Include(v => v.Employee)
                .FirstOrDefaultAsync(v => v.Id == id && v.Status == VisitStatus.Expected);
            if (Found is null) return RedirectToPage();
            await LoadListsAsync();
            return Page();
        }

        try
        {
            if (!await visits.CheckInAsync(id, identity!.Value.Phone, capture!, StayMinutes))
            {
                TempData["Error"] = "That appointment can no longer be checked in.";
                return RedirectToPage();
            }
        }
        catch (VisitorBlacklistedException ex)
        {
            return await BlockAsync(ex.Entry);
        }
        catch (LicenseLimitException ex)
        {
            return await LimitReachedAsync(ex);
        }
        return RedirectToPage("/Visits/Badge", new { id, kiosk = true, t = badgeTokens.Issue(tenant.Id, id) });
    }

    // Walk-in visitors.
    public async Task<IActionResult> OnPostWalkInAsync()
    {
        ModelState.Remove(nameof(AccessCode));
        var identity = ValidateIdentity();
        ValidateStayAndAgreement();

        if (identity is not null && await blacklist.FindAsync(identity.Value.Phone) is { } entry)
            return await BlockAsync(entry);

        var capture = await ValidateLocationAsync(ValidateCapture(identity?.Photo, identity is not null));
        ValidateWalkInFields();
        if (!(await lookups.ActiveNamesAsync(LookupCategory.Purpose)).Contains(Visit.Purpose))
            ModelState.AddModelError("Visit.Purpose", "Please choose a purpose.");

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }

        Visit.Phone = identity!.Value.Phone;
        try
        {
            var saved = await visits.CheckInWalkInAsync(Visit, capture!, StayMinutes);
            return RedirectToPage("/Visits/Badge", new { id = saved.Id, kiosk = true, t = badgeTokens.Issue(tenant.Id, saved.Id) });
        }
        catch (VisitorBlacklistedException ex)
        {
            return await BlockAsync(ex.Entry);
        }
        catch (LicenseLimitException ex)
        {
            return await LimitReachedAsync(ex);
        }
    }

    private async Task<IActionResult> LimitReachedAsync(LicenseLimitException ex)
    {
        logger.LogWarning("Tenant {Tenant}: check-in refused, plan limit: {Reason}", tenant.Slug, ex.Message);
        ModelState.Clear();
        LimitMessage = IsStaff
            ? ex.Message
            : "We can't check you in at this kiosk right now. Please speak to a member of reception staff.";
        Found = null;
        PhotoData = null;
        SignatureData = null;
        BusinessCardImageData = null;
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSendOtpAsync(string? phone)
    {
        var normalized = PhoneVerification.Normalize(phone);
        if (normalized is null)
            return new JsonResult(new { ok = false, error = "Please enter a valid phone number." });

        await otp.SendAsync(normalized);
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostVerifyOtpAsync(string? phone, string? code)
    {
        var normalized = PhoneVerification.Normalize(phone);
        if (normalized is null || string.IsNullOrWhiteSpace(code) || !await otp.VerifyAsync(normalized, code))
            return new JsonResult(new { ok = false, error = "That code is incorrect. Please try again." });

        return new JsonResult(new { ok = true, token = phoneVerification.IssueToken(normalized) });
    }

    /// <summary>
    /// Shows a neutral message rather than saying "blacklisted" at a public kiosk,
    /// and drops everything the visitor entered.
    /// </summary>
    private async Task<IActionResult> BlockAsync(BlacklistEntry entry)
    {
        logger.LogWarning("Kiosk check-in refused for blacklisted visitor {Name} ({Phone})", entry.Name, entry.Phone);
        ModelState.Clear();
        Blocked = true;
        Found = null;
        Visit = new Visit();
        PhotoData = null;
        PhoneToken = null;
        SignatureData = null;
        BusinessCardImageData = null;
        await LoadListsAsync();
        return Page();
    }

    private void PrefillFrom(Visit appointment)
    {
        Visit.Phone = appointment.Phone; // prefill; it still has to be verified
        StayMinutes = VisitService.PlannedStayMinutes(appointment) ?? StayMinutes;
    }

    /// <summary>Checks the photo and the OTP-verified phone, adding model errors for anything missing.</summary>
    /// <summary>Checks the OTP-verified phone and (per Admin › Form fields) the photo. Null if something required is missing.</summary>
    private (string Phone, byte[]? Photo)? ValidateIdentity()
    {
        var phone = PhoneVerification.Normalize(Visit.Phone);
        if (phone is null)
            ModelState.AddModelError("Visit.Phone", "Please enter your mobile number.");
        else if (!phoneVerification.IsValid(PhoneToken, phone))
            ModelState.AddModelError("Visit.Phone", "Please verify your mobile number with the code we send you.");

        byte[]? photo = null;
        if (!Fields.Shown("checkin.photo")) PhotoData = null;
        else if (PhotoCapture.TryParse(PhotoData, out var jpeg)) photo = jpeg;
        else if (Fields.Required("checkin.photo") || !string.IsNullOrEmpty(PhotoData))
            ModelState.AddModelError(nameof(PhotoData), "Please take your photo.");

        if (phone is null || (photo is null && Fields.Required("checkin.photo"))) return null;
        return (phone, photo);
    }

    /// <summary>Signature and business card image, per Admin › Form fields. Null if something required is missing.</summary>
    private CheckInCapture? ValidateCapture(byte[]? photo, bool identityOk)
    {
        if (!CardScanEnabled) BusinessCardImageData = null;

        byte[]? signature = null;
        if (!Fields.Shown("checkin.signature")) SignatureData = null;
        else if (PhotoCapture.TryParsePng(SignatureData, out var png)) signature = png;
        else if (Fields.Required("checkin.signature"))
            ModelState.AddModelError(nameof(SignatureData), "Please sign to accept the visitor agreement.");

        byte[]? cardImage = null;
        if (!string.IsNullOrEmpty(BusinessCardImageData))
        {
            if (PhotoCapture.TryParse(BusinessCardImageData, out var jpeg)) cardImage = jpeg;
            else BusinessCardImageData = null; // optional: drop an unreadable image rather than block check-in
        }

        if (!identityOk || (signature is null && Fields.Required("checkin.signature"))) return null;
        return new CheckInCapture(photo, signature, cardImage);
    }

    /// <summary>Company and email on the walk-in form, per Admin › Form fields.</summary>
    private void ValidateWalkInFields()
    {
        void Check(string key, Func<string?> get, Action<string?> set, string field, string label)
        {
            if (!Fields.Shown(key)) { set(null); ModelState.Remove(field); return; }
            if (Fields.Required(key) && string.IsNullOrWhiteSpace(get()))
                ModelState.AddModelError(field, $"Please enter your {label}.");
        }
        Check("checkin.company", () => Visit.Company, v => Visit.Company = v, "Visit.Company", "company");
        Check("checkin.email", () => Visit.Email, v => Visit.Email = v, "Visit.Email", "email address");
    }

    /// <summary>Building and floor (Admin › Form fields): hidden, optional or required; a value must be an active option.</summary>
    private async Task<CheckInCapture?> ValidateLocationAsync(CheckInCapture? capture)
    {
        var mode = Fields["checkin.location"];
        string? Pick(string? value, List<string> options, string field, string label)
        {
            if (mode == FieldMode.Hidden || options.Count == 0) return null;
            if (string.IsNullOrWhiteSpace(value) && mode == FieldMode.Optional) return null;
            var match = options.FirstOrDefault(o => o == value?.Trim());
            if (match is null) ModelState.AddModelError(field, $"Please choose the {label} you're visiting.");
            return match;
        }
        var building = Pick(Building, await lookups.ActiveNamesAsync(LookupCategory.Building), nameof(Building), "building");
        var floor = Pick(Floor, await lookups.ActiveNamesAsync(LookupCategory.Floor), nameof(Floor), "floor");
        return capture is null ? null : capture with { Building = building, Floor = floor };
    }

    private void ValidateStayAndAgreement()
    {
        if (!Fields.Shown("checkin.stay"))
            StayMinutes = (Found is not null ? VisitService.PlannedStayMinutes(Found) : null) ?? ExpectedStay.DefaultMinutes;
        else if (!ExpectedStay.IsValid(StayMinutes))
            ModelState.AddModelError(nameof(StayMinutes), "Please choose how long you expect to stay.");
        if (!AcceptAgreement)
            ModelState.AddModelError(nameof(AcceptAgreement), "Please accept the visitor agreement to continue.");
    }

    private async Task LoadListsAsync()
    {
        Hosts = await db.Employees.Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync();
        Purposes = await lookups.OptionsIncludingAsync(LookupCategory.Purpose, Found?.Purpose ?? Visit.Purpose);
        StayOptions = await lookups.StayOptionsAsync();
        Buildings = await lookups.ActiveNamesAsync(LookupCategory.Building);
        Floors = await lookups.ActiveNamesAsync(LookupCategory.Floor);
        if (string.IsNullOrEmpty(Visit.Purpose)) Visit.Purpose = Purposes.FirstOrDefault() ?? "";
        if (HttpMethods.IsGet(Request.Method) && Found is null) StayMinutes = ExpectedStay.DefaultFrom(StayOptions);
    }
}
