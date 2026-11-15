namespace VisitorManagement.Services;

public enum FieldMode
{
    Hidden,
    Optional,
    Required
}

/// <summary>A configurable form field: which modes the admin may choose and the default.</summary>
public record FieldInfo(string Key, string Screen, string Name, string Description, FieldMode Default, FieldMode[] Allowed);

/// <summary>
/// Which fields the check-in (kiosk) and appointment screens show, chosen in Admin › Form fields.
/// Name, mobile number (OTP), host, purpose and the agreement are always required.
/// </summary>
public class FieldSettings(IReadOnlyDictionary<string, FieldMode> modes)
{
    public const string CheckIn = "Check-in (kiosk)";
    public const string Appointment = "New appointment";

    private static readonly FieldMode[] Any = [FieldMode.Hidden, FieldMode.Optional, FieldMode.Required];
    private static readonly FieldMode[] ShowHide = [FieldMode.Hidden, FieldMode.Optional];
    private static readonly FieldMode[] ShowRequire = [FieldMode.Hidden, FieldMode.Required];

    public static readonly IReadOnlyList<FieldInfo> All =
    [
        new("checkin.company", CheckIn, "Company", "Walk-in visitor's company.", FieldMode.Optional, Any),
        new("checkin.email", CheckIn, "Email", "Walk-in visitor's email address.", FieldMode.Optional, Any),
        new("checkin.stay", CheckIn, "Expected stay", "Sets the expected check-out time. Hidden: the default stay is used.", FieldMode.Required, ShowRequire),
        new("checkin.location", CheckIn, "Building & floor", "Where the visitor will be; used by the evacuation roll call.", FieldMode.Required, Any),
        new("checkin.photo", CheckIn, "Visitor photo", "Webcam photo shown on the badge and in staff views.", FieldMode.Required, Any),
        new("checkin.signature", CheckIn, "Signature", "Signature accepting the visitor agreement (the agreement tick is always required).", FieldMode.Required, Any),
        new("checkin.bcard", CheckIn, "Business card scan", "Scan a business card to fill in the walk-in form (needs the plan feature).", FieldMode.Optional, ShowHide),
        new("appt.phone", Appointment, "Mobile number", "Used for phone look-up at the kiosk and blacklist checks.", FieldMode.Required, [FieldMode.Optional, FieldMode.Required]),
        new("appt.company", Appointment, "Company", "Visitor's company.", FieldMode.Optional, Any),
        new("appt.email", Appointment, "Email", "Visitor's email address.", FieldMode.Optional, Any),
        new("appt.stay", Appointment, "Expected stay", "Planned length of the visit. Hidden: the default stay is used.", FieldMode.Required, ShowRequire),
        new("appt.notes", Appointment, "Notes for reception", "Free-text notes shown to reception.", FieldMode.Optional, Any),
    ];

    public static FieldSettings Defaults { get; } = new(new Dictionary<string, FieldMode>());

    public FieldMode this[string key]
    {
        get
        {
            var info = All.First(f => f.Key == key);
            return modes.TryGetValue(key, out var m) && info.Allowed.Contains(m) ? m : info.Default;
        }
    }

    public bool Shown(string key) => this[key] != FieldMode.Hidden;
    public bool Required(string key) => this[key] == FieldMode.Required;

    /// <summary>Stored as "key=Mode;key=Mode".</summary>
    public string Serialize() => string.Join(';', All.Select(f => $"{f.Key}={this[f.Key]}"));

    public static FieldSettings Parse(string? value)
    {
        var modes = new Dictionary<string, FieldMode>();
        foreach (var part in (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && Enum.TryParse<FieldMode>(kv[1], out var m)) modes[kv[0]] = m;
        }
        return new FieldSettings(modes);
    }
}
