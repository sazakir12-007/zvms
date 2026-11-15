using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>A single setting for one tenant (branding, pass design, ...), stored as text. Key: (TenantId, Key).</summary>
public class AppSetting : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    [Required, StringLength(100)]
    public string Key { get; set; } = "";

    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
