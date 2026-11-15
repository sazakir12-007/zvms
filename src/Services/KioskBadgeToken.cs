using Microsoft.AspNetCore.DataProtection;

namespace VisitorManagement.Services;

/// <summary>
/// Lets a visitor at the (signed-out) kiosk see their own badge and photo straight after checking in,
/// for a few minutes, without exposing anyone else's. The token is signed and names one visit.
/// </summary>
public class KioskBadgeToken(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("VisitorManagement.KioskBadge").ToTimeLimitedDataProtector();

    public string Issue(int tenantId, int visitId) => _protector.Protect($"{tenantId}:{visitId}", Lifetime);

    public bool IsValidFor(string? token, int tenantId, int visitId)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            return _protector.Unprotect(token) == $"{tenantId}:{visitId}";
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false; // tampered or expired
        }
    }
}
