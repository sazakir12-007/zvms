using VisitorManagement.Models;

namespace VisitorManagement;

public static class Format
{
    /// <summary>Currency symbol for plan prices and credit (config "Billing:CurrencySymbol").</summary>
    public static string CurrencySymbol { get; set; } = "₹";

    /// <summary>Number grouping for money (config "Billing:Culture"); en-IN groups as 1,00,000.</summary>
    public static System.Globalization.CultureInfo MoneyCulture { get; set; } = System.Globalization.CultureInfo.GetCultureInfo("en-IN");

    /// <summary>₹1,499 for whole amounts, ₹6.25 otherwise.</summary>
    public static string Money(decimal amount) =>
        (amount < 0 ? "-" : "") + CurrencySymbol + Math.Abs(amount).ToString(amount % 1 == 0 ? "N0" : "N2", MoneyCulture);

    /// <summary>Per-visitor prices keep up to 3 decimals (₹0.075).</summary>
    public static string UnitPrice(decimal amount) => CurrencySymbol + amount.ToString("#,##0.00#", MoneyCulture);

    /// <summary>
    /// CSS class that colours a badge, card or pill by visit purpose. The colours come from
    /// /lookups/purposes.css, generated from Admin &gt; Lists.
    /// </summary>
    public static string PurposeClass(string? purpose)
    {
        var slug = new string((purpose ?? "").ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return "purpose-" + (slug.Length == 0 ? "none" : slug);
    }

    public static string StatusLabel(VisitStatus status) => status switch
    {
        VisitStatus.Expected => "Expected",
        VisitStatus.CheckedIn => "Checked in",
        VisitStatus.CheckedOut => "Checked out",
        VisitStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string StatusClass(VisitStatus status) => status switch
    {
        VisitStatus.CheckedIn => "text-bg-success",
        VisitStatus.Expected => "text-bg-warning",
        VisitStatus.Cancelled => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    /// <summary>"Jamie Lee Test" -> "JT", for avatars when there is no photo.</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    /// <summary>"just now", "5 min ago", "2 h ago", "yesterday", or the date.</summary>
    public static string Ago(DateTime at)
    {
        var span = DateTime.Now - at;
        return span switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)span.TotalMinutes} min ago",
            { TotalHours: < 24 } => $"{(int)span.TotalHours} h ago",
            { TotalDays: < 2 } => "yesterday",
            _ => at.ToString("d")
        };
    }

    public static string Duration(TimeSpan? span) => span switch
    {
        null => "",
        { TotalHours: >= 1 } s => $"{(int)s.TotalHours}h {s.Minutes}m",
        var s => $"{s.Value.Minutes}m"
    };
}
