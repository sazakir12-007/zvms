using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

public enum ReportType
{
    VisitRegister,
    DailySummary,
    ByDepartment,
    ByHost,
    ByPurpose,
    Overstays,
    FrequentVisitors,
    Blacklist
}

public record ReportInfo(ReportType Type, string Title, string Description, string Icon);

public record ReportRequest(
    ReportType Type,
    DateTime From,
    DateTime To,
    string? Department = null,
    int? HostId = null,
    string? Purpose = null);

public record ReportColumn(string Header, bool Numeric = false);

public record ReportResult(
    ReportInfo Info,
    string Period,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string?[]> Rows,
    string?[]? Totals = null,
    ChartData? Chart = null,
    bool ChartIsTimeline = false);

/// <summary>Builds the reports on the Reports page. Works in memory on the chosen period's visits.</summary>
public class ReportService(AppDbContext db, LookupService lookups)
{
    public static readonly IReadOnlyList<ReportInfo> Catalog =
    [
        new(ReportType.VisitRegister, "Visit register", "Every visit with times, host and status. The audit record.", "log"),
        new(ReportType.DailySummary, "Daily summary", "Visits, unique visitors, walk-ins, no-shows and overstays per day.", "calendar"),
        new(ReportType.ByDepartment, "By department", "Visit volume and time on site for each department.", "building"),
        new(ReportType.ByHost, "By host", "Which staff receive the most visitors.", "user"),
        new(ReportType.ByPurpose, "By purpose", "Meetings, interviews, deliveries, contractors and more.", "badge"),
        new(ReportType.Overstays, "Overstays", "Visitors who stayed past their expected check-out, and by how long.", "alert"),
        new(ReportType.FrequentVisitors, "Frequent visitors", "Repeat visitors, matched by mobile number.", "users"),
        new(ReportType.Blacklist, "Blacklist", "Everyone blacklisted, with reasons and dates. Ignores the filters.", "ban"),
    ];

    public static ReportInfo InfoFor(ReportType type) => Catalog.First(r => r.Type == type);

    public async Task<ReportResult> RunAsync(ReportRequest r)
    {
        var info = InfoFor(r.Type);
        var from = r.From.Date;
        var to = r.To.Date.AddDays(1);
        var period = $"{r.From:d} – {r.To:d}";

        if (r.Type == ReportType.Blacklist)
            return await BlacklistReportAsync(info);

        // Everything that happened, or was due to happen, in the period.
        var query = db.Visits.AsNoTracking().Include(v => v.Employee)
            .Where(v => (v.CheckedInAt >= from && v.CheckedInAt < to)
                     || (v.CheckedInAt == null && v.ExpectedAt >= from && v.ExpectedAt < to));
        if (!string.IsNullOrWhiteSpace(r.Department)) query = query.Where(v => v.Department == r.Department);
        if (r.HostId is { } hostId) query = query.Where(v => v.EmployeeId == hostId);
        if (!string.IsNullOrWhiteSpace(r.Purpose)) query = query.Where(v => v.Purpose == r.Purpose);

        var all = await query.ToListAsync();
        var arrived = all.Where(v => v.CheckedInAt is not null).ToList();

        return r.Type switch
        {
            ReportType.VisitRegister => Register(info, period, all),
            ReportType.DailySummary => Daily(info, period, all, r.From.Date, r.To.Date),
            ReportType.ByDepartment => Grouped(info, period, arrived, v => DepartmentName(v.Department), "Department"),
            ReportType.ByHost => Grouped(info, period, arrived, v => v.Employee?.Name ?? "(unknown)", "Host"),
            ReportType.ByPurpose => Grouped(info, period, arrived, v => v.Purpose, "Purpose",
                order: (await lookups.ActiveNamesAsync(LookupCategory.Purpose))
                    .Concat(arrived.Select(v => v.Purpose).Distinct().Order()).Distinct().ToArray(),
                cssClass: Format.PurposeClass),
            ReportType.Overstays => Overstays(info, period, arrived),
            ReportType.FrequentVisitors => Frequent(info, period, arrived),
            _ => throw new ArgumentOutOfRangeException(nameof(r))
        };
    }

    private static ReportResult Register(ReportInfo info, string period, List<Visit> visits)
    {
        var rows = visits.OrderBy(v => v.CheckedInAt ?? v.ExpectedAt).Select(v => new[]
        {
            (v.CheckedInAt ?? v.ExpectedAt)?.ToString("g"),
            v.VisitorName, v.Company, v.Phone, v.Employee?.Name, v.Department, v.Location, v.Purpose, v.Status.ToString(),
            v.CheckedInAt?.ToString("t"), v.ExpectedCheckOutAt?.ToString("t"), v.CheckedOutAt?.ToString("t"),
            Format.Duration(v.Duration), v.Overstay is { } o ? Format.Duration(o) : "", v.BadgeNumber
        }).ToList<string?[]>();

        return new ReportResult(info, period,
            [new("Date"), new("Visitor"), new("Company"), new("Phone"), new("Host"), new("Department"), new("Location"), new("Purpose"),
             new("Status"), new("In"), new("Expected out"), new("Out"), new("Duration", true), new("Overstay", true), new("Badge")],
            rows,
            Totals: ["Total", $"{visits.Count} visits", null, null, null, null, null, null, null, null, null, null, null,
                     $"{visits.Count(v => v.Overstay is not null)} overstays", null]);
    }

    private static ReportResult Daily(ReportInfo info, string period, List<Visit> visits, DateTime first, DateTime last)
    {
        var today = DateTime.Today;
        var days = Enumerable.Range(0, (last - first).Days + 1).Select(i => first.AddDays(i)).ToList();
        var rows = new List<string?[]>();
        var points = new List<ChartPoint>();

        foreach (var d in days)
        {
            var arrived = visits.Where(v => v.CheckedInAt?.Date == d).ToList();
            var noShows = d < today
                ? visits.Count(v => v.CheckedInAt is null && v.Status == VisitStatus.Expected && v.ExpectedAt?.Date == d)
                : 0;
            rows.Add([
                d.ToString("ddd d MMM yyyy"),
                arrived.Count.ToString(),
                arrived.Select(VisitorKey).Distinct().Count().ToString(),
                arrived.Count(v => v.AccessCode is null).ToString(),
                arrived.Count(v => v.AccessCode is not null).ToString(),
                noShows.ToString(),
                AverageDuration(arrived),
                arrived.Count(v => v.Overstay is not null).ToString()
            ]);
            points.Add(new ChartPoint(d.ToString(days.Count > 31 ? "d/M" : "ddd d"), arrived.Count,
                $"{d:ddd d MMM}: {arrived.Count} visit{(arrived.Count == 1 ? "" : "s")}"));
        }

        var allArrived = visits.Where(v => v.CheckedInAt is not null).ToList();
        string?[] totals =
        [
            "Total",
            allArrived.Count.ToString(),
            allArrived.Select(VisitorKey).Distinct().Count().ToString(),
            allArrived.Count(v => v.AccessCode is null).ToString(),
            allArrived.Count(v => v.AccessCode is not null).ToString(),
            visits.Count(v => v.CheckedInAt is null && v.Status == VisitStatus.Expected && v.ExpectedAt < today).ToString(),
            AverageDuration(allArrived),
            allArrived.Count(v => v.Overstay is not null).ToString()
        ];

        return new ReportResult(info, period,
            [new("Day"), new("Visits", true), new("Unique visitors", true), new("Walk-ins", true), new("Appointments", true),
             new("No-shows", true), new("Avg duration", true), new("Overstays", true)],
            rows, totals,
            new ChartData("Visits per day", period, points), ChartIsTimeline: true);
    }

    private static ReportResult Grouped(ReportInfo info, string period, List<Visit> visits, Func<Visit, string> key, string label,
        string[]? order = null, Func<string, string?>? cssClass = null)
    {
        var groups = visits.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
        if (order is not null)
            foreach (var name in order) groups.TryAdd(name, []);

        var ordered = order is not null
            ? order.Select(n => (Name: n, Visits: groups[n]))
            : groups.Select(g => (Name: g.Key, Visits: g.Value)).OrderByDescending(g => g.Visits.Count).ThenBy(g => g.Name);

        var list = ordered.ToList();
        var rows = list.Select(g => new[]
        {
            g.Name,
            g.Visits.Count.ToString(),
            visits.Count == 0 ? "0%" : ((double)g.Visits.Count / visits.Count).ToString("P0"),
            g.Visits.Select(VisitorKey).Distinct().Count().ToString(),
            AverageDuration(g.Visits),
            TotalHours(g.Visits),
            g.Visits.Count(v => v.Overstay is not null).ToString()
        }).ToList<string?[]>();

        var chart = new ChartData($"Visits by {label.ToLowerInvariant()}", period,
            list.Select(g => new ChartPoint(g.Name, g.Visits.Count, CssClass: cssClass?.Invoke(g.Name))).ToList());

        return new ReportResult(info, period,
            [new(label), new("Visits", true), new("Share", true), new("Unique visitors", true), new("Avg duration", true),
             new("Total hours", true), new("Overstays", true)],
            rows,
            ["Total", visits.Count.ToString(), "100%", visits.Select(VisitorKey).Distinct().Count().ToString(),
             AverageDuration(visits), TotalHours(visits), visits.Count(v => v.Overstay is not null).ToString()],
            chart);
    }

    private static ReportResult Overstays(ReportInfo info, string period, List<Visit> visits)
    {
        var over = visits.Where(v => v.Overstay is not null).OrderByDescending(v => v.Overstay).ToList();
        var rows = over.Select(v => new[]
        {
            v.VisitorName, v.Phone, v.Employee?.Name, v.Department, v.Purpose,
            v.CheckedInAt?.ToString("g"), v.ExpectedCheckOutAt?.ToString("t"),
            v.CheckedOutAt?.ToString("t") ?? "Still on site", Format.Duration(v.Overstay)
        }).ToList<string?[]>();

        var avg = over.Count == 0 ? "" : Format.Duration(TimeSpan.FromMinutes(over.Average(v => v.Overstay!.Value.TotalMinutes)));
        return new ReportResult(info, period,
            [new("Visitor"), new("Phone"), new("Host"), new("Department"), new("Purpose"), new("Checked in"),
             new("Expected out"), new("Checked out"), new("Overstay", true)],
            rows,
            ["Total", $"{over.Count} of {visits.Count} visits", null, null, null, null, null, "Average", avg]);
    }

    private static ReportResult Frequent(ReportInfo info, string period, List<Visit> visits)
    {
        var groups = visits.GroupBy(VisitorKey)
            .Select(g => g.OrderByDescending(v => v.CheckedInAt).ToList())
            .OrderByDescending(g => g.Count).ThenByDescending(g => g[0].CheckedInAt)
            .ToList();

        var rows = groups.Select(g => new[]
        {
            g[0].VisitorName, g[0].Phone, g[0].Company,
            g.Count.ToString(),
            g.Last().CheckedInAt?.ToString("d"), g[0].CheckedInAt?.ToString("d"),
            TotalHours(g),
            string.Join(", ", g.Select(v => v.Employee?.Name).Distinct().Take(3))
        }).ToList<string?[]>();

        return new ReportResult(info, period,
            [new("Visitor"), new("Phone"), new("Company"), new("Visits", true), new("First visit"), new("Last visit"),
             new("Total hours", true), new("Hosts")],
            rows,
            ["Total", $"{groups.Count} visitors", null, visits.Count.ToString(), null, null, TotalHours(visits), null]);
    }

    private async Task<ReportResult> BlacklistReportAsync(ReportInfo info)
    {
        var entries = await db.BlacklistEntries.AsNoTracking().OrderByDescending(b => b.CreatedAt).ToListAsync();
        var rows = entries.Select(b => new[]
        {
            b.Name, b.Phone, b.Reason, b.CreatedAt.ToString("g"), b.IsActive ? "Active" : "Removed", b.RemovedAt?.ToString("g")
        }).ToList<string?[]>();

        return new ReportResult(info, "All time",
            [new("Visitor"), new("Mobile"), new("Reason"), new("Added"), new("Status"), new("Removed")],
            rows,
            ["Total", $"{entries.Count(b => b.IsActive)} active, {entries.Count(b => !b.IsActive)} removed", null, null, null, null]);
    }

    /// <summary>Same person across visits: their phone's last 10 digits, else their name.</summary>
    private static string VisitorKey(Visit v)
    {
        var digits = new string((v.Phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? digits[^Math.Min(10, digits.Length)..] : v.VisitorName.Trim().ToLowerInvariant();
    }

    private static string DepartmentName(string? d) => string.IsNullOrWhiteSpace(d) ? HostPickerModel.NoDepartment : d;

    private static string AverageDuration(IEnumerable<Visit> visits)
    {
        var done = visits.Where(v => v.CheckedOutAt is not null).ToList();
        return done.Count == 0 ? "" : Format.Duration(TimeSpan.FromMinutes(done.Average(v => v.Duration!.Value.TotalMinutes)));
    }

    private static string TotalHours(IEnumerable<Visit> visits) =>
        visits.Where(v => v.Duration is not null).Sum(v => v.Duration!.Value.TotalHours).ToString("0.0");
}
