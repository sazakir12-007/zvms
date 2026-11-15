using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

public static class StaffRoles
{
    /// <summary>Everything, including hosts, dropdown lists, branding and staff users.</summary>
    public const string Admin = "Admin";

    /// <summary>Day-to-day reception work: check-in, appointments, visitor log, reports, blacklist.</summary>
    public const string Reception = "Reception";

    public static readonly string[] All = [Admin, Reception];
}

/// <summary>A member of staff who signs in to the app.</summary>
public class StaffUser : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = "";

    /// <summary>Sign-in name. Stored lower-case.</summary>
    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Phone, StringLength(30)]
    public string? Phone { get; set; }

    [Required, StringLength(20)]
    public string Role { get; set; } = StaffRoles.Reception;

    [Required]
    public string PasswordHash { get; set; } = "";

    /// <summary>Set for a new or reset account: the user must choose their own password at next sign-in.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Changes whenever the password, role or active state changes. Sign-in cookies carry it,
    /// so changing it signs the user out everywhere.
    /// </summary>
    [Required, StringLength(40)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool IsActive { get; set; } = true;

    public int FailedSignIns { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastSignInAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
