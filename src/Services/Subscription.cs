namespace VisitorManagement.Services;

public enum SubscriptionState
{
    /// <summary>No expiry date set.</summary>
    Unlimited,
    Active,
    /// <summary>Ends within <see cref="Subscription.WarnDays"/> days.</summary>
    ExpiringSoon,
    /// <summary>Ended, still inside the grace period: everything works, with warnings.</summary>
    Grace,
    /// <summary>Grace period over: check-in is blocked until renewed.</summary>
    Expired
}

/// <summary>Subscription period rules (option 1 licensing: an expiry date per organisation, renewed by payment).</summary>
public static class Subscription
{
    public const int GraceDays = 7;
    public const int WarnDays = 7;
    public const int TrialDays = 30;
    public static readonly int[] RenewalMonths = [1, 3, 6, 12];

    public static SubscriptionState State(DateTime? validUntil, DateTime? today = null)
    {
        if (validUntil is not { } until) return SubscriptionState.Unlimited;
        var d = (today ?? DateTime.Today).Date;
        if (d <= until.Date) return (until.Date - d).Days < WarnDays ? SubscriptionState.ExpiringSoon : SubscriptionState.Active;
        return d <= until.Date.AddDays(GraceDays) ? SubscriptionState.Grace : SubscriptionState.Expired;
    }

    public static DateTime GraceEnds(DateTime validUntil) => validUntil.Date.AddDays(GraceDays);

    /// <summary>New end date after renewing for <paramref name="months"/>: from the current end, or from today if already ended.</summary>
    public static DateTime Extend(DateTime? validUntil, int months)
    {
        var from = validUntil is { } u && u.Date >= DateTime.Today ? u.Date : DateTime.Today.AddDays(-1);
        return from.AddMonths(months);
    }

    public static int DaysLeft(DateTime validUntil) => (validUntil.Date - DateTime.Today).Days;
}
