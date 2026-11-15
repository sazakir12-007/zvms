using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace VisitorManagement.Services;

/// <summary>
/// After a correct OTP the browser gets a signed, short-lived token naming the phone number.
/// The check-in form posts it back, so the server can trust the number was verified
/// without keeping any session state.
/// </summary>
public partial class PhoneVerification(IDataProtectionProvider provider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("VisitorManagement.PhoneVerification").ToTimeLimitedDataProtector();

    /// <summary>Strips formatting so "+1 (555) 010-1234" and "+15550101234" compare equal. Null if not a plausible number.</summary>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmed = phone.Trim();
        var digits = NonDigits().Replace(trimmed, "");
        if (digits.Length is < 7 or > 15) return null;
        return trimmed.StartsWith('+') ? "+" + digits : digits;
    }

    /// <summary>
    /// True if two numbers are the same line, tolerating formatting and a missing country code:
    /// "555-010-1234" matches "+1 555 010 1234". Compares the last 10 digits, or all digits for shorter numbers.
    /// </summary>
    public static bool SameNumber(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var x = NonDigits().Replace(a, "");
        var y = NonDigits().Replace(b, "");
        if (x.Length < 7 || y.Length < 7) return false;
        if (x.Length >= 10 && y.Length >= 10) return x[^10..] == y[^10..];
        return x == y;
    }

    public string IssueToken(string normalizedPhone) => _protector.Protect(normalizedPhone, Lifetime);

    public bool IsValid(string? token, string? phone)
    {
        var normalized = Normalize(phone);
        if (string.IsNullOrEmpty(token) || normalized is null) return false;
        try
        {
            return _protector.Unprotect(token) == normalized;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false; // tampered or expired
        }
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
