using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>A staff member who can host visitors.</summary>
public class Employee : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [StringLength(100)]
    public string? Department { get; set; }

    [Phone, StringLength(30)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Visit> Visits { get; set; } = new();
}
