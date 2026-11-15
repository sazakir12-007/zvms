using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>Staff accounts: password checks, lockout, and the claims carried in the sign-in cookie.</summary>
public class StaffAuthService(AppDbContext db, ILogger<StaffAuthService> logger)
{
    public const int MinPasswordLength = 8;
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(10);

    public const string StampClaim = "vms:stamp";
    public const string TenantClaim = "vms:tenant";
    public const string MustChangePasswordClaim = "vms:must-change-password";

    private static readonly PasswordHasher<StaffUser> Hasher = new();

    // Verified against when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly string DummyHash = Hasher.HashPassword(new StaffUser(), Guid.NewGuid().ToString());

    public Task<bool> AnyUsersAsync() => db.StaffUsers.AnyAsync();

    public Task<StaffUser?> FindAsync(int id) => db.StaffUsers.FirstOrDefaultAsync(u => u.Id == id);

    public static int? CurrentUserId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Checks an email and password. On failure the message is deliberately vague.</summary>
    public async Task<(StaffUser? User, string? Error)> CheckSignInAsync(string? email, string? password)
    {
        const string invalid = "That email and password don't match an active account.";
        email = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || !user.IsActive)
        {
            Hasher.VerifyHashedPassword(new StaffUser(), DummyHash, password ?? "");
            return (null, invalid);
        }

        if (user.LockedUntil > DateTime.Now)
            return (null, $"Too many failed attempts. Try again after {user.LockedUntil:t}, or ask an admin to reset your password.");

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedSignIns++;
            if (user.FailedSignIns >= MaxFailedSignIns)
            {
                user.LockedUntil = DateTime.Now + LockoutDuration;
                user.FailedSignIns = 0;
                logger.LogWarning("Locked staff account {Email} after {Count} failed sign-ins", user.Email, MaxFailedSignIns);
            }
            await db.SaveChangesAsync();
            return (null, invalid);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = Hasher.HashPassword(user, password!);

        user.FailedSignIns = 0;
        user.LockedUntil = null;
        user.LastSignInAt = DateTime.Now;
        await db.SaveChangesAsync();
        logger.LogInformation("Staff sign-in: {Email}", user.Email);
        return (user, null);
    }

    /// <summary>
    /// Re-checks the signed-in user's password before a sensitive action (e.g. a data export).
    /// Wrong attempts count towards the same lockout as sign-in.
    /// </summary>
    public async Task<string?> ConfirmPasswordAsync(int userId, string? password)
    {
        var user = await FindAsync(userId);
        if (user is null || !user.IsActive) return "Account not found.";
        if (user.LockedUntil > DateTime.Now) return $"Too many failed attempts. Try again after {user.LockedUntil:t}.";

        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
        {
            if (++user.FailedSignIns >= MaxFailedSignIns)
            {
                user.LockedUntil = DateTime.Now + LockoutDuration;
                user.FailedSignIns = 0;
            }
            await db.SaveChangesAsync();
            return "That password is incorrect.";
        }

        user.FailedSignIns = 0;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Null if the password is acceptable, otherwise why not.</summary>
    public static string? CheckPasswordRules(string? password, string? email)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return $"Use at least {MinPasswordLength} characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Use a mix of letters and numbers.";
        if (!string.IsNullOrEmpty(email) && password.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase))
            return "Don't base the password on your email address.";
        return null;
    }

    public async Task<(StaffUser? User, string? Error)> CreateAsync(string name, string email, string role, string password, bool mustChangePassword)
    {
        email = email.Trim().ToLowerInvariant();
        if (!StaffRoles.All.Contains(role)) return (null, "Unknown role.");
        if (await db.StaffUsers.AnyAsync(u => u.Email == email)) return (null, "A staff account with this email already exists.");
        if (CheckPasswordRules(password, email) is { } rule) return (null, rule);

        var user = new StaffUser { Name = name.Trim(), Email = email, Role = role, MustChangePassword = mustChangePassword };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.StaffUsers.Add(user);
        await db.SaveChangesAsync();
        logger.LogInformation("Created staff account {Email} ({Role})", email, role);
        return (user, null);
    }

    public async Task<string?> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await FindAsync(userId);
        if (user is null) return "Account not found.";
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return "Your current password is incorrect.";
        if (newPassword == currentPassword) return "Choose a password different from your current one.";
        if (CheckPasswordRules(newPassword, user.Email) is { } rule) return rule;

        SetPassword(user, newPassword, mustChange: false);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Admin reset: sets a temporary password the user must change, and signs them out everywhere.</summary>
    public async Task<string?> ResetPasswordAsync(int userId, string temporaryPassword)
    {
        var user = await FindAsync(userId);
        if (user is null) return "Account not found.";
        if (CheckPasswordRules(temporaryPassword, user.Email) is { } rule) return rule;

        SetPassword(user, temporaryPassword, mustChange: true);
        user.LockedUntil = null;
        user.FailedSignIns = 0;
        await db.SaveChangesAsync();
        logger.LogInformation("Password reset for staff account {Email}", user.Email);
        return null;
    }

    public async Task<string?> UpdateAsync(int userId, string name, string role, bool isActive, int actingUserId)
    {
        var user = await FindAsync(userId);
        if (user is null) return "Account not found.";
        if (!StaffRoles.All.Contains(role)) return "Unknown role.";

        var losesAdmin = user.Role == StaffRoles.Admin && user.IsActive && (role != StaffRoles.Admin || !isActive);
        if (losesAdmin && !await db.StaffUsers.AnyAsync(u => u.Id != userId && u.Role == StaffRoles.Admin && u.IsActive))
            return "There must always be at least one active admin.";
        if (userId == actingUserId && !isActive)
            return "You can't deactivate your own account.";

        var signOut = user.Role != role || user.IsActive != isActive;
        user.Name = name.Trim();
        user.Role = role;
        user.IsActive = isActive;
        if (signOut) user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> UpdateProfileAsync(int userId, string name, string? phone)
    {
        var user = await FindAsync(userId);
        if (user is null) return "Account not found.";
        user.Name = name.Trim();
        user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        await db.SaveChangesAsync();
        return null;
    }

    public static ClaimsPrincipal CreatePrincipal(StaffUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(StampClaim, user.SecurityStamp),
            new(TenantClaim, user.TenantId.ToString())
        };
        if (user.MustChangePassword) claims.Add(new Claim(MustChangePasswordClaim, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// Runs on every request with a sign-in cookie: rejects it if it was issued by a different tenant,
    /// or the account has been deactivated, or its password / role changed since the cookie was issued.
    /// </summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal is { } p ? CurrentUserId(p) : null;
        var stamp = context.Principal?.FindFirstValue(StampClaim);
        var tenant = context.HttpContext.RequestServices.GetRequiredService<TenantContext>();
        var sameTenant = tenant.HasTenant && context.Principal?.FindFirstValue(TenantClaim) == tenant.Id.ToString();

        // The query is tenant-filtered too, so a user from another tenant is never found.
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = id is null || !sameTenant ? null : await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

        if (user is null || !user.IsActive || user.SecurityStamp != stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static void SetPassword(StaffUser user, string password, bool mustChange)
    {
        user.PasswordHash = Hasher.HashPassword(user, password);
        user.MustChangePassword = mustChange;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }
}
