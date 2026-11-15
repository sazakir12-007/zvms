using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>
/// A visitor who must not be checked in. Matched on mobile number, which every visitor
/// verifies by OTP at check-in. Entries are deactivated rather than deleted to keep an audit trail.
/// </summary>
public class BlacklistEntry : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>Normalised mobile number (see PhoneVerification.Normalize).</summary>
    [Required, StringLength(30)]
    public string Phone { get; set; } = "";

    [Required, StringLength(500)]
    public string Reason { get; set; } = "";

    /// <summary>The visit this entry was raised from, if any (gives staff the visitor's photo).</summary>
    public int? VisitId { get; set; }
    public Visit? Visit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;

    public DateTime? RemovedAt { get; set; }
}
