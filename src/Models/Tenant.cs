using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace VisitorManagement.Models;

/// <summary>
/// An organisation using the app. Its short <see cref="Slug"/> is the first part of every URL
/// (e.g. /acme/CheckIn). All its data lives in the shared database, tagged with its <see cref="Id"/>.
/// </summary>
public partial class Tenant
{
    public int Id { get; set; }

    /// <summary>Short id used in URLs: lowercase letters, digits and hyphens.</summary>
    [Required, StringLength(30)]
    public string Slug { get; set; } = "";

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>License package: features, monthly price and visitor allowance.</summary>
    public int? LicensePackageId { get; set; }

    /// <summary>
    /// Subscription end (end of this day). Null: no expiry. After it, a grace period of
    /// <see cref="Subscription.GraceDays"/> days, then check-in stops until renewed.
    /// </summary>
    public DateTime? ValidUntil { get; set; }
    public LicensePackage? LicensePackage { get; set; }

    /// <summary>
    /// Words that can't be tenant ids because the app uses them as top-level paths
    /// (or might, for static files and future features).
    /// </summary>
    public static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "platform", "payments", "account", "admin", "api", "app", "css", "js", "lib", "img", "images", "static", "assets",
        "favicon.ico", "robots.txt", "lookups", "branding", "visits", "notifications", "help", "setup",
        "login", "logout", "error", "health", "www", "t", "tenant", "tenants", "_framework", "_content"
    };

    public const string SlugRules = "2–30 characters: lowercase letters, numbers and hyphens, starting with a letter or number.";

    /// <summary>Null if the slug is acceptable, otherwise why not.</summary>
    public static string? CheckSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || !SlugPattern().IsMatch(slug)) return $"The ID must be {SlugRules}";
        if (ReservedSlugs.Contains(slug)) return $"\"{slug}\" is reserved. Choose another ID.";
        return null;
    }

    public static string NormalizeSlug(string? slug) => (slug ?? "").Trim().ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,29}$")]
    public static partial Regex SlugPattern();
}

/// <summary>Data that belongs to one tenant. The database filters and stamps these automatically.</summary>
public interface ITenantOwned
{
    int TenantId { get; set; }
}

/// <summary>Someone who manages tenants (not tied to any tenant).</summary>
public class PlatformAdmin
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Required]
    public string PasswordHash { get; set; } = "";

    [Required, StringLength(40)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public int FailedSignIns { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
