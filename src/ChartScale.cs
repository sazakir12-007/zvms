using System.Globalization;

namespace VisitorManagement;

public static class ChartScale
{
    /// <summary>Rounds an axis maximum up to a clean, even number (4 -> 4, 7 -> 8, 13 -> 20, 230 -> 300).</summary>
    public static double NiceMax(double max)
    {
        if (max <= 0) return 0;
        if (max <= 4) return Math.Ceiling(max / 2) * 2;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
        {
            var candidate = step * magnitude;
            if (candidate >= max) return candidate;
        }
        return 10 * magnitude;
    }

    /// <summary>1234 -> "1,234"; 12900 -> "12.9K"; whole numbers stay whole.</summary>
    public static string Format(double value) => value switch
    {
        >= 10_000 => (value / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ when value == Math.Floor(value) => value.ToString("N0", CultureInfo.CurrentCulture),
        _ => value.ToString("0.#", CultureInfo.CurrentCulture)
    };
}
