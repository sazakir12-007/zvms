using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>
/// Platform admins manage tenants. They use their own sign-in cookie (scheme "Platform", path /platform),
/// completely separate from tenant staff accounts, and can't see inside tenants.
/// </summary>
public class PlatformAuthService(AppDbContext db, ILogger<PlatformAuthService> logger)
{
    public const string Scheme = "Platform";
    public const string Role = "PlatformAdmin";

    private static readonly PasswordHasher<PlatformAdmin> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(new PlatformAdmin(), Guid.NewGuid().ToString());

    public Task<bool> AnyAdminsAsync() => db.PlatformAdmins.AnyAsync();

    public async Task<(PlatformAdmin? Admin, string? Error)> CreateAsync(string name, string email, string password)
    {
        email = email.Trim().ToLowerInvariant();
        if (await db.PlatformAdmins.AnyAsync(a => a.Email == email)) return (null, "That email is already a platform admin.");
        if (StaffAuthService.CheckPasswordRules(password, email) is { } rule) return (null, rule);

        var admin = new PlatformAdmin { Name = name.Trim(), Email = email };
        admin.PasswordHash = Hasher.HashPassword(admin, password);
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync();
        logger.LogInformation("Created platform admin {Email}", email);
        return (admin, null);
    }

    public async Task<(PlatformAdmin? Admin, string? Error)> CheckSignInAsync(string? email, string? password)
    {
        const string invalid = "That email and password don't match a platform admin.";
        email = (email ?? "").Trim().ToLowerInvariant();
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Email == email);
        if (admin is null)
        {
            Hasher.VerifyHashedPassword(new PlatformAdmin(), DummyHash, password ?? "");
            return (null, invalid);
        }
        if (admin.LockedUntil > DateTime.Now)
            return (null, $"Too many failed attempts. Try again after {admin.LockedUntil:t}.");

        if (Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
        {
            if (++admin.FailedSignIns >= StaffAuthService.MaxFailedSignIns)
            {
                admin.LockedUntil = DateTime.Now + StaffAuthService.LockoutDuration;
                admin.FailedSignIns = 0;
                logger.LogWarning("Locked platform admin {Email}", admin.Email);
            }
            await db.SaveChangesAsync();
            return (null, invalid);
        }

        admin.FailedSignIns = 0;
        admin.LockedUntil = null;
        await db.SaveChangesAsync();
        return (admin, null);
    }

    /// <summary>Re-checks the platform admin's password before a backup is created or downloaded.</summary>
    public async Task<string?> ConfirmPasswordAsync(int adminId, string? password)
    {
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == adminId);
        if (admin is null) return "Account not found.";
        if (admin.LockedUntil > DateTime.Now) return $"Too many failed attempts. Try again after {admin.LockedUntil:t}.";

        if (Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
        {
            if (++admin.FailedSignIns >= StaffAuthService.MaxFailedSignIns)
            {
                admin.LockedUntil = DateTime.Now + StaffAuthService.LockoutDuration;
                admin.FailedSignIns = 0;
            }
            await db.SaveChangesAsync();
            return "That password is incorrect.";
        }

        admin.FailedSignIns = 0;
        await db.SaveChangesAsync();
        return null;
    }

    public static ClaimsPrincipal CreatePrincipal(PlatformAdmin admin) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
        new Claim(ClaimTypes.Name, admin.Name),
        new Claim(ClaimTypes.Email, admin.Email),
        new Claim(ClaimTypes.Role, Role),
        new Claim(StaffAuthService.StampClaim, admin.SecurityStamp)
    ], Scheme));

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal is { } p ? StaffAuthService.CurrentUserId(p) : null;
        var stamp = context.Principal?.FindFirstValue(StaffAuthService.StampClaim);
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var admin = id is null ? null : await db.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (admin is null || admin.SecurityStamp != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }
}
