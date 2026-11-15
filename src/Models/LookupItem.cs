using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>The admin-managed dropdown lists.</summary>
public enum LookupCategory
{
    Purpose,
    Department,
    StayDuration,
    DocumentType,
    Building,
    Floor
}

/// <summary>
/// One option in an admin-managed dropdown. Visits and hosts store the option's name as text,
/// so history stays readable if an option is later deactivated.
/// </summary>
public class LookupItem : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int Id { get; set; }

    public LookupCategory Category { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = "";

    /// <summary>Badge colour (#rrggbb). Purposes only.</summary>
    [StringLength(7)]
    public string? Color { get; set; }

    /// <summary>Length of stay. Stay durations only; Name is derived from it.</summary>
    public int? Minutes { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>A file captured at check-in, other than the visitor's photo.</summary>
public enum AttachmentKind
{
    Signature,
    /// <summary>ID card photo (no longer captured; kept for older visits).</summary>
    IdCard,
    BusinessCard
}

public class VisitAttachment : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int VisitId { get; set; }

    public AttachmentKind Kind { get; set; }

    public byte[] Data { get; set; } = [];

    public string ContentType { get; set; } = "image/png";

    public DateTime CapturedAt { get; set; } = DateTime.Now;
}
