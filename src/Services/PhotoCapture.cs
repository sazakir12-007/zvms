namespace VisitorManagement.Services;

/// <summary>Parses the image data URLs the check-in widgets post (webcam photos, ID card photo, signature).</summary>
public static class PhotoCapture
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>JPEG from a webcam capture (visitor photo or ID card).</summary>
    public static bool TryParse(string? dataUrl, out byte[] jpeg) =>
        TryParse(dataUrl, "data:image/jpeg;base64,", [0xFF, 0xD8, 0xFF], out jpeg);

    /// <summary>PNG from the signature pad.</summary>
    public static bool TryParsePng(string? dataUrl, out byte[] png) =>
        TryParse(dataUrl, "data:image/png;base64,", [0x89, 0x50, 0x4E, 0x47], out png);

    private static bool TryParse(string? dataUrl, string prefix, byte[] magic, out byte[] bytes)
    {
        bytes = [];
        if (dataUrl is null || !dataUrl.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var base64 = dataUrl.AsSpan(prefix.Length);
        if (base64.Length > MaxBytes * 4 / 3 + 4) return false;

        var buffer = new byte[base64.Length * 3 / 4];
        if (!Convert.TryFromBase64Chars(base64, buffer, out var written)) return false;
        if (written < magic.Length || !buffer.AsSpan(0, magic.Length).SequenceEqual(magic)) return false;

        bytes = buffer[..written];
        return true;
    }

    /// <summary>
    /// Keeps only the last 4 letters/digits of an ID number ("D1234-567-890" -> "••••7890"),
    /// so the full number is never stored. Null if there is nothing usable.
    /// </summary>
    public static string? MaskIdNumber(string? raw)
    {
        var chars = (raw ?? "").Where(char.IsLetterOrDigit).ToArray();
        return chars.Length < 4 ? null : "••••" + new string(chars[^4..]).ToUpperInvariant();
    }
}
