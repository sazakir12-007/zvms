namespace VisitorManagement;

/// <summary>
/// How long a visitor expects to stay, which sets their expected check-out time.
/// The options offered come from Admin &gt; Lists; these are the hard limits.
/// </summary>
public static class ExpectedStay
{
    public const int DefaultMinutes = 60;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 24 * 60;

    public static bool IsValid(int minutes) => minutes is >= MinMinutes and <= MaxMinutes;

    /// <summary>The default if it's offered, otherwise the closest option.</summary>
    public static int DefaultFrom(IReadOnlyList<int> options) =>
        options.Count == 0 || options.Contains(DefaultMinutes)
            ? DefaultMinutes
            : options.OrderBy(m => Math.Abs(m - DefaultMinutes)).First();

    /// <summary>90 -> "1 h 30 min".</summary>
    public static string Label(int minutes) => (minutes / 60, minutes % 60) switch
    {
        (0, var m) => $"{m} min",
        (var h, 0) => h == 1 ? "1 hour" : $"{h} hours",
        var (h, m) => $"{h} h {m} min"
    };
}

/// <param name="IdPrefix">Makes element ids unique when several pickers are on one page.</param>
/// <param name="Options">Active stay options in minutes (Admin &gt; Lists).</param>
public record ExpectedStayModel(int Minutes, string IdPrefix, IReadOnlyList<int> Options);

/// <param name="Hosts">Active hosts; their departments fill the department list.</param>
/// <param name="SelectedHostId">Host to preselect (0 for none).</param>
/// <param name="IdPrefix">Makes element ids unique when several pickers are on one page.</param>
public record HostPickerModel(IReadOnlyList<Models.Employee> Hosts, int SelectedHostId, string IdPrefix)
{
    public const string NoDepartment = "General";

    public static string DepartmentOf(Models.Employee e) =>
        string.IsNullOrWhiteSpace(e.Department) ? NoDepartment : e.Department.Trim();

    public IEnumerable<string> Departments =>
        Hosts.Select(DepartmentOf).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
}
