using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

public enum VisitStatus
{
    Expected,
    CheckedIn,
    CheckedOut,
    Cancelled
}

public class Visit : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Full name")]
    public string VisitorName { get; set; } = "";

    [StringLength(100)]
    public string? Company { get; set; }

    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [Phone, StringLength(30)]
    public string? Phone { get; set; }

    /// <summary>True once the visitor proved ownership of <see cref="Phone"/> with an OTP.</summary>
    public bool PhoneVerified { get; set; }

    public VisitPhoto? Photo { get; set; }

    [Display(Name = "Host")]
    [Range(1, int.MaxValue, ErrorMessage = "Please choose who you are visiting.")]
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Host's department at the time of the visit, kept so reports don't change if the host moves.</summary>
    [StringLength(100)]
    public string? Department { get; set; }

    /// <summary>Where the visitor is (Admin › Dropdown lists › Buildings), for the evacuation report.</summary>
    [StringLength(60)]
    public string? Building { get; set; }

    [StringLength(60)]
    public string? Floor { get; set; }

    public string? Location => (Building, Floor) switch
    {
        (null or "", null or "") => null,
        (null or "", var f) => f,
        (var b, null or "") => b,
        var (b, f) => $"{b} · {f}"
    };

    /// <summary>Name of a Purpose option from Admin &gt; Lists.</summary>
    [Required(ErrorMessage = "Please choose a purpose.")]
    public string Purpose { get; set; } = "";

    /// <summary>Type of ID shown at check-in, if the visitor scanned or photographed one.</summary>
    [StringLength(50)]
    public string? IdDocumentType { get; set; }

    /// <summary>Last 4 characters of the ID number only (e.g. "••••1234"). The full number is never stored.</summary>
    [StringLength(20)]
    public string? IdNumberMasked { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public VisitStatus Status { get; set; } = VisitStatus.Expected;

    /// <summary>Scheduled arrival for appointments.</summary>
    [Display(Name = "Expected arrival")]
    public DateTime? ExpectedAt { get; set; }

    public DateTime? CheckedInAt { get; set; }

    /// <summary>When the visitor said they would leave. Planned for appointments, fixed at check-in.</summary>
    [Display(Name = "Expected check-out")]
    public DateTime? ExpectedCheckOutAt { get; set; }

    public DateTime? CheckedOutAt { get; set; }

    [StringLength(20)]
    public string? BadgeNumber { get; set; }

    /// <summary>Short code an appointment visitor types at the kiosk.</summary>
    [StringLength(8)]
    public string? AccessCode { get; set; }

    public bool AgreementAccepted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public TimeSpan? Duration =>
        CheckedInAt is null ? null : (CheckedOutAt ?? DateTime.Now) - CheckedInAt.Value;

    /// <summary>How long past the expected check-out the visitor stayed (or has stayed so far). Null if not over.</summary>
    public TimeSpan? Overstay
    {
        get
        {
            if (CheckedInAt is null || ExpectedCheckOutAt is null || Status == VisitStatus.Cancelled) return null;
            var leftAt = CheckedOutAt ?? DateTime.Now;
            return leftAt > ExpectedCheckOutAt ? leftAt - ExpectedCheckOutAt.Value : null;
        }
    }

    /// <summary>Still on site after the expected check-out time.</summary>
    public bool IsOverstaying => Status == VisitStatus.CheckedIn && Overstay is not null;
}
