using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <param name="Id">Stable id (kind + visit), so the browser can tell which ones it has seen.</param>
/// <param name="Level">"danger", "warning" or "info".</param>
/// <param name="At">When the situation started; newer than the last-seen time means unread.</param>
public record AppNotification(string Id, string Level, string Icon, string Title, string Detail, string Url, DateTime At);

/// <summary>
/// Things reception should know about right now, worked out from current visits
/// (nothing is stored): blacklisted visitors on site, overstays, late appointments, recent arrivals.
/// </summary>
public class NotificationService(AppDbContext db, BlacklistService blacklist, TenantContext tenant, LicenseService license)
{
    private static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RecentArrival = TimeSpan.FromHours(1);

    public async Task<List<AppNotification>> GetAsync()
    {
        var now = DateTime.Now;
        var today = DateTime.Today;
        var list = new List<AppNotification>();

        var plan = await license.GetStatusAsync();
        if (plan.ValidUntil is { } until && plan.Subscription is SubscriptionState.ExpiringSoon or SubscriptionState.Grace or SubscriptionState.Expired)
        {
            var (title, detail) = plan.Subscription switch
            {
                SubscriptionState.Expired => ("Subscription expired", "Visitor check-in is paused until the subscription is renewed."),
                SubscriptionState.Grace => ("Subscription ended", $"Ended on {until:d}. Check-in stops after {Subscription.GraceEnds(until):d}."),
                _ => ("Subscription ending soon", $"Ends on {until:d} ({Subscription.DaysLeft(until)} day(s) left).")
            };
            list.Add(new AppNotification($"sub-{plan.Subscription}-{until:yyyyMMdd}",
                plan.Subscription == SubscriptionState.ExpiringSoon ? "warning" : "danger", "package", title, detail, tenant.Url("/Plan"), now));
        }
        if (!plan.Unlimited && plan.RemainingCheckIns == 0)
        {
            list.Add(new AppNotification($"plan-limit-{plan.PeriodStart:yyyyMM}", "danger", "package",
                "Check-in is paused",
                plan.MaxRemaining == 0
                    ? $"This month's maximum of {plan.Package!.MaxVisitorsPerMonth:N0} check-ins is reached."
                    : "Included check-ins are used up and the credit balance is used up. Buy credit to continue.",
                tenant.Url("/Plan"), now));
        }
        else if (plan.RunningLow)
        {
            list.Add(new AppNotification($"plan-low-{plan.PeriodStart:yyyyMM}", "warning", "package",
                "Check-ins running low",
                $"{plan.RemainingCheckIns:N0} check-ins left this month on the {plan.Package!.Name} plan.",
                tenant.Url("/Plan"), now));
        }

        var onSite = await db.Visits.AsNoTracking().Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.CheckedIn)
            .ToListAsync();
        var blacklisted = await blacklist.BlacklistedVisitIdsAsync(onSite);

        foreach (var v in onSite.Where(v => blacklisted.Contains(v.Id)))
        {
            list.Add(new AppNotification($"blacklisted-{v.Id}", "danger", "ban",
                $"Blacklisted visitor on site: {v.VisitorName}",
                $"Host {v.Employee?.Name} · badge {v.BadgeNumber}",
                tenant.Url($"/Visits/Badge/{v.Id}"), v.CheckedInAt ?? now));
        }

        foreach (var v in onSite.Where(v => v.IsOverstaying).OrderByDescending(v => v.Overstay))
        {
            list.Add(new AppNotification($"overstay-{v.Id}", "warning", "alert",
                $"{v.VisitorName} is overstaying",
                $"Expected out {v.ExpectedCheckOutAt:t} · over by {Format.Duration(v.Overstay)} · host {v.Employee?.Name}",
                tenant.Url("/?show=overstaying#on-site"), v.ExpectedCheckOutAt ?? now));
        }

        var lateCutoff = now - LateAfter;
        var late = await db.Visits.AsNoTracking().Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.Expected && v.ExpectedAt >= today && v.ExpectedAt < lateCutoff)
            .ToListAsync();
        foreach (var v in late)
        {
            list.Add(new AppNotification($"late-{v.Id}", "warning", "clock",
                $"{v.VisitorName} hasn't arrived",
                $"Appointment at {v.ExpectedAt:t} with {v.Employee?.Name}",
                tenant.Url("/Appointments"), v.ExpectedAt!.Value + LateAfter));
        }

        foreach (var v in onSite.Where(v => v.CheckedInAt >= now - RecentArrival).OrderByDescending(v => v.CheckedInAt))
        {
            list.Add(new AppNotification($"arrived-{v.Id}", "info", "check-in",
                $"{v.VisitorName} checked in",
                $"{v.Purpose} · host {v.Employee?.Name} · {v.CheckedInAt:t}",
                tenant.Url($"/Visits/Badge/{v.Id}"), v.CheckedInAt!.Value));
        }

        // Most urgent first, newest first within a level.
        static int Rank(string level) => level switch { "danger" => 0, "warning" => 1, _ => 2 };
        return list.OrderBy(n => Rank(n.Level)).ThenByDescending(n => n.At).ToList();
    }
}
