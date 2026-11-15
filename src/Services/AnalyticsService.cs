using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>One bar or column in a chart.</summary>
/// <param name="Tooltip">Hover text; defaults to "Label: Value".</param>
/// <param name="CssClass">Colours the mark, e.g. a purpose class. Null uses the default series colour.</param>
public record ChartPoint(string Label, double Value, string? Tooltip = null, string? CssClass = null);

/// <param name="ValueSuffix">Unit shown after values in labels and the table, e.g. " min".</param>
public record ChartData(string Title, string Subtitle, IReadOnlyList<ChartPoint> Points, string ValueSuffix = "")
{
    public double Max => Points.Count == 0 ? 0 : Points.Max(p => p.Value);
    public double Total => Points.Sum(p => p.Value);
}

public record DashboardAnalytics(
    ChartData VisitsPerDay,
    ChartData ByPurpose,
    ChartData ByHour,
    ChartData ByDepartment,
    double AverageMinutes,
    int OverstaysLast30Days,
    int VisitsLast30Days);

/// <summary>Numbers behind the dashboard charts. Works on checked-in visits in memory (fine at reception scale).</summary>
public class AnalyticsService(AppDbContext db, LookupService lookups)
{
    public const int Days = 30;
    private const int TrendDays = 14;
    private const int TopDepartments = 6;

    public async Task<DashboardAnalytics> GetDashboardAsync()
    {
        var today = DateTime.Today;
        var from = today.AddDays(-(Days - 1));
        var visits = await db.Visits.AsNoTracking()
            .Where(v => v.CheckedInAt >= from && v.Status != VisitStatus.Cancelled)
            .ToListAsync();

        // Visits per day, last 14 days, every day present even when zero.
        var perDay = Enumerable.Range(0, TrendDays)
            .Select(i => today.AddDays(i - (TrendDays - 1)))
            .Select(d =>
            {
                var n = visits.Count(v => v.CheckedInAt!.Value.Date == d);
                return new ChartPoint(d.ToString("ddd d"), n, $"{d:ddd d MMM}: {n} visit{(n == 1 ? "" : "s")}");
            })
            .ToList();

        // Purpose: every active purpose in admin order (so colours and positions don't shift),
        // plus any retired purpose that still has visits in the period.
        var purposeNames = (await lookups.ActiveNamesAsync(LookupCategory.Purpose))
            .Concat(visits.Select(v => v.Purpose).Distinct().Order())
            .Distinct()
            .ToList();
        var byPurpose = purposeNames
            .Select(p => new ChartPoint(p, visits.Count(v => v.Purpose == p), CssClass: Format.PurposeClass(p)))
            .ToList();

        // Arrivals by hour of day across the period, office hours plus any busy edges.
        var hours = visits.Select(v => v.CheckedInAt!.Value.Hour).ToList();
        var firstHour = Math.Min(7, hours.DefaultIfEmpty(7).Min());
        var lastHour = Math.Max(19, hours.DefaultIfEmpty(19).Max());
        var byHour = Enumerable.Range(firstHour, lastHour - firstHour + 1)
            .Select(h =>
            {
                var n = hours.Count(x => x == h);
                return new ChartPoint($"{h:00}", n, $"{h:00}:00–{h:00}:59: {n} arrival{(n == 1 ? "" : "s")}");
            })
            .ToList();

        // Departments: top N by visits, the rest folded into "Other".
        var deptGroups = visits
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Department) ? HostPickerModel.NoDepartment : v.Department!)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Name)
            .ToList();
        var byDepartment = deptGroups.Take(TopDepartments)
            .Select(g => new ChartPoint(g.Name, g.Count))
            .ToList();
        var other = deptGroups.Skip(TopDepartments).Sum(g => g.Count);
        if (other > 0) byDepartment.Add(new ChartPoint("Other", other));

        var finished = visits.Where(v => v.CheckedOutAt is not null).ToList();
        var averageMinutes = finished.Count == 0 ? 0 : finished.Average(v => v.Duration!.Value.TotalMinutes);

        return new DashboardAnalytics(
            new ChartData("Visits per day", $"Check-ins, last {TrendDays} days", perDay),
            new ChartData("Visits by purpose", $"Last {Days} days", byPurpose),
            new ChartData("Busiest hours", $"Arrivals by hour of day, last {Days} days", byHour),
            new ChartData("Visits by department", $"Last {Days} days", byDepartment),
            averageMinutes,
            visits.Count(v => v.Overstay is not null),
            visits.Count);
    }
}
