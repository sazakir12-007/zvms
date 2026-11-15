using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

public enum BrandDisplay
{
    LogoAndText,
    LogoOnly,
    TextOnly
}

public enum BannerStyle
{
    Light,
    Gradient,
    Dark
}

/// <param name="LogoVersion">Changes whenever the logo changes, for cache-busting its URL. Null when no logo is uploaded.</param>
/// <param name="BasePath">The tenant's URL prefix, e.g. "/acme".</param>
public record Branding(
    string CompanyName,
    string? Tagline,
    BrandDisplay Display,
    BannerStyle Style,
    string? LogoVersion,
    string BasePath = "")
{
    public bool HasLogo => LogoVersion is not null;
    public bool ShowLogo => Display != BrandDisplay.TextOnly;
    public bool ShowText => Display != BrandDisplay.LogoOnly;
    public string LogoUrl => $"{BasePath}/branding/logo?v={LogoVersion}";
}

public enum PassTemplate
{
    Classic,
    Modern,
    Lanyard,
    Minimal
}

/// <param name="WidthIn">Printed size in inches (sets the print page size).</param>
public record PassTemplateInfo(PassTemplate Template, string Name, string Description, double WidthIn, double HeightIn)
{
    public string Size => $"{WidthIn:0.##} × {HeightIn:0.##} in";

    /// <summary>CSS page size, always with "." decimals regardless of the server's culture.</summary>
    public string CssSize => FormattableString.Invariant($"{WidthIn}in {HeightIn}in");
}

/// <summary>Visitor pass design chosen in Admin › Pass templates.</summary>
public record PassSettings(
    PassTemplate Template,
    bool ShowLogo,
    bool ShowPhoto,
    bool ShowCompany,
    bool ShowHost,
    bool ShowValidUntil,
    string FooterText)
{
    public const string DefaultFooter = "Valid for date shown only · Escort required";

    public static readonly IReadOnlyList<PassTemplateInfo> Templates =
    [
        new(PassTemplate.Classic, "Classic", "Coloured header and purpose band, photo beside the details.", 4, 3),
        new(PassTemplate.Modern, "Modern", "Purpose colour down the side, round photo, clean layout.", 4, 3),
        new(PassTemplate.Lanyard, "Lanyard", "Portrait card for badge holders and lanyards; large photo.", 3, 4),
        new(PassTemplate.Minimal, "Minimal label", "Black-only sticker for thermal label printers (e.g. Dymo, Brother).", 4, 2.25),
    ];

    public PassTemplateInfo Info => Templates.First(t => t.Template == Template);

    /// <summary>Classes that hide the parts switched off in settings.</summary>
    public string OptionClasses => string.Join(' ', new[]
    {
        ShowLogo ? null : "no-logo",
        ShowPhoto ? null : "no-photo",
        ShowCompany ? null : "no-company",
        ShowHost ? null : "no-host",
        ShowValidUntil ? null : "no-until"
    }.Where(c => c is not null));
}

/// <summary>Key/value application settings (branding), cached in memory.</summary>
public class SettingsService(AppDbContext db, IMemoryCache cache, TenantContext tenant, LicenseService license)
{
    public const int MaxLogoBytes = 512 * 1024;
    private string CacheKey => $"settings:{tenant.Id}";

    private static class Keys
    {
        public const string CompanyName = "Branding.CompanyName";
        public const string Tagline = "Branding.Tagline";
        public const string Display = "Branding.Display";
        public const string Style = "Branding.Style";
        public const string LogoBase64 = "Branding.LogoBase64";
        public const string LogoContentType = "Branding.LogoContentType";
        public const string PassTemplate = "Pass.Template";
        public const string PassOptions = "Pass.Options";
        public const string PassFooter = "Pass.Footer";
        public const string Fields = "Form.Fields";
    }

    /// <summary>
    /// Banner shown in the top bar. Without the Custom branding feature, the organisation's name with the
    /// built-in mark and the default style; saved settings are kept for if the plan changes back.
    /// </summary>
    public async Task<Branding> GetBrandingAsync()
    {
        var s = await AllAsync();
        if (tenant.HasTenant && !await license.HasFeatureAsync(Feature.CustomBranding))
            return new Branding(tenant.Current?.Name ?? "Your Company", "Visitor Management",
                BrandDisplay.LogoAndText, BannerStyle.Light, null, "/" + tenant.Slug);

        var logoVersion = s.TryGetValue(Keys.LogoBase64, out var logo) && !string.IsNullOrEmpty(logo.Value)
            ? logo.UpdatedAt.Ticks.ToString("x")
            : null;
        return new Branding(
            Value(s, Keys.CompanyName) ?? tenant.Current?.Name ?? "Your Company",
            s.ContainsKey(Keys.Tagline) ? Value(s, Keys.Tagline) : "Visitor Management",
            Enum.TryParse<BrandDisplay>(Value(s, Keys.Display), out var d) ? d : BrandDisplay.LogoAndText,
            Enum.TryParse<BannerStyle>(Value(s, Keys.Style), out var st) ? st : BannerStyle.Light,
            logoVersion,
            tenant.HasTenant ? "/" + tenant.Slug : "");
    }

    public async Task SaveBrandingAsync(string companyName, string? tagline, BrandDisplay display, BannerStyle style)
    {
        await SetAsync(Keys.CompanyName, companyName.Trim());
        await SetAsync(Keys.Tagline, string.IsNullOrWhiteSpace(tagline) ? "" : tagline.Trim());
        await SetAsync(Keys.Display, display.ToString());
        await SetAsync(Keys.Style, style.ToString());
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    public async Task<(byte[] Data, string ContentType)?> GetLogoAsync()
    {
        var s = await AllAsync();
        var b64 = Value(s, Keys.LogoBase64);
        var type = Value(s, Keys.LogoContentType);
        return string.IsNullOrEmpty(b64) || string.IsNullOrEmpty(type) ? null : (Convert.FromBase64String(b64), type);
    }

    /// <summary>Stores a PNG / JPEG / GIF / WebP logo. Returns an error message, or null on success.</summary>
    /// <remarks>SVG is refused: opened directly it could run script on this site.</remarks>
    public async Task<string?> SaveLogoAsync(byte[] data)
    {
        if (data.Length == 0) return "The file is empty.";
        if (data.Length > MaxLogoBytes) return $"The logo must be at most {MaxLogoBytes / 1024} KB.";
        var type = SniffImageType(data);
        if (type is null) return "Please upload a PNG, JPEG, GIF or WebP image.";

        await SetAsync(Keys.LogoBase64, Convert.ToBase64String(data));
        await SetAsync(Keys.LogoContentType, type);
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
        return null;
    }

    public async Task RemoveLogoAsync()
    {
        await SetAsync(Keys.LogoBase64, null);
        await SetAsync(Keys.LogoContentType, null);
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    /// <summary>Which optional fields the check-in and appointment screens show (Admin › Form fields).</summary>
    public async Task<FieldSettings> GetFieldSettingsAsync() =>
        FieldSettings.Parse(Value(await AllAsync(), Keys.Fields));

    public async Task SaveFieldSettingsAsync(FieldSettings fields)
    {
        await SetAsync(Keys.Fields, fields.Serialize());
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    /// <summary>Visitor pass design. Without the Pass templates feature, the Classic pass with everything shown.</summary>
    public async Task<PassSettings> GetPassSettingsAsync()
    {
        if (tenant.HasTenant && !await license.HasFeatureAsync(Feature.PassTemplates))
            return new PassSettings(PassTemplate.Classic, true, true, true, true, true, PassSettings.DefaultFooter);
        var s = await AllAsync();
        // Options are stored as a list of what's switched OFF, so new options default to on.
        var off = (Value(s, Keys.PassOptions) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return new PassSettings(
            Enum.TryParse<PassTemplate>(Value(s, Keys.PassTemplate), out var t) ? t : PassTemplate.Classic,
            !off.Contains("logo"), !off.Contains("photo"), !off.Contains("company"), !off.Contains("host"), !off.Contains("until"),
            s.ContainsKey(Keys.PassFooter) ? Value(s, Keys.PassFooter) ?? "" : PassSettings.DefaultFooter);
    }

    public async Task SavePassSettingsAsync(PassSettings p)
    {
        var off = new[]
        {
            p.ShowLogo ? null : "logo", p.ShowPhoto ? null : "photo", p.ShowCompany ? null : "company",
            p.ShowHost ? null : "host", p.ShowValidUntil ? null : "until"
        }.Where(x => x is not null);
        await SetAsync(Keys.PassTemplate, p.Template.ToString());
        await SetAsync(Keys.PassOptions, string.Join(',', off));
        await SetAsync(Keys.PassFooter, p.FooterText.Trim());
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    private async Task<Dictionary<string, AppSetting>> AllAsync() =>
        await cache.GetOrCreateAsync(CacheKey, async _ =>
            await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key)) ?? new();

    private static string? Value(Dictionary<string, AppSetting> s, string key) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v.Value) ? v.Value : null;

    private async Task SetAsync(string key, string? value)
    {
        // Key is (TenantId, Key); the query filter limits this to the current tenant.
        var row = db.AppSettings.Local.FirstOrDefault(s => s.Key == key && s.TenantId == tenant.Id)
            ?? await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.Now;
        }
    }

    private static string? SniffImageType(byte[] d) => d switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null
    };
}
