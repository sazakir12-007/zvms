using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>
/// The current tenant's plan for this billing period (calendar month).
/// Check-ins up to <see cref="LicensePackage.IncludedVisitors"/> are covered by the base price; after that each
/// check-in is charged <see cref="LicensePackage.CostPerExtraVisitor"/> from the prepaid credit balance (money);
/// <see cref="LicensePackage.MaxVisitorsPerMonth"/> is a hard cap.
/// </summary>
public record LicenseStatus(
    LicensePackage? Package,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int CheckInsThisPeriod,
    int IncludedRemaining,
    decimal CreditBalance,
    int ExtraCheckInsThisPeriod,
    decimal CreditUsedThisPeriod,
    decimal CreditsBoughtThisPeriod,
    decimal CostThisPeriod,
    DateTime? ValidUntil = null)
{
    public SubscriptionState Subscription => Services.Subscription.State(ValidUntil);
    public bool SubscriptionBlocked => Subscription == SubscriptionState.Expired;

    /// <summary>No package assigned: unrestricted (only possible for data from before licensing).</summary>
    public bool Unlimited => Package is null;

    public int MaxRemaining => Package is null ? int.MaxValue : Math.Max(0, Package.MaxVisitorsPerMonth - CheckInsThisPeriod);

    /// <summary>Extra check-ins the credit balance pays for (unlimited when extra visitors are free).</summary>
    public int CheckInsFromCredit => Package is null || Package.CostPerExtraVisitor <= 0
        ? int.MaxValue
        : (int)Math.Floor(Math.Max(0, CreditBalance) / Package.CostPerExtraVisitor);

    /// <summary>How many more visitors can check in this month: included + what the credit covers, capped by the monthly maximum.</summary>
    public int RemainingCheckIns => Package is null
        ? int.MaxValue
        : (int)Math.Min(MaxRemaining, (long)IncludedRemaining + CheckInsFromCredit);

    /// <summary>Under 10% of this month's possible check-ins left (for warnings).</summary>
    public bool RunningLow => Package is not null && RemainingCheckIns > 0 && RemainingCheckIns < MaxRemaining
        && RemainingCheckIns <= Math.Max(5, (Package.IncludedVisitors + Math.Min(CheckInsFromCredit, Package.MaxVisitorsPerMonth)) / 10);

    public bool Has(Feature f) => Package is null || Package.Has(f);
}

public class LicenseLimitException(string reason) : InvalidOperationException(reason);

public enum CheckInCharge
{
    Included,
    Credit
}

/// <summary>Plan, features and credits for the current tenant.</summary>
public class LicenseService(AppDbContext db, TenantContext tenant, ILogger<LicenseService> logger)
{
    private LicenseStatus? _status;

    public static (DateTime Start, DateTime End) CurrentPeriod()
    {
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return (start, start.AddMonths(1));
    }

    public async Task<LicenseStatus> GetStatusAsync()
    {
        if (_status is not null) return _status;

        var package = tenant.Current?.LicensePackageId is { } pid
            ? await db.LicensePackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid)
            : null;
        var (start, end) = CurrentPeriod();

        var checkIns = await db.Visits.CountAsync(v => v.CheckedInAt >= start && v.CheckedInAt < end);
        // SQLite can't sum decimals in SQL, so add up in memory (one small row per purchase / extra check-in).
        var ledger = await db.CreditTransactions.AsNoTracking()
            .Select(t => new { t.Kind, t.Amount, t.CreatedAt })
            .ToListAsync();
        var balance = ledger.Sum(t => t.Amount);
        var period = ledger.Where(t => t.CreatedAt >= start && t.CreatedAt < end).ToList();

        var bought = period.Where(t => t.Kind == CreditKind.Purchase).Sum(t => t.Amount);
        var usage = period.Where(t => t.Kind == CreditKind.Usage).ToList();
        var included = package is null ? 0 : Math.Max(0, package.IncludedVisitors - checkIns);

        return _status = new LicenseStatus(package, start, end, checkIns, included, balance,
            usage.Count, -usage.Sum(t => t.Amount), bought, (package?.BasePricePerMonth ?? 0) + bought, tenant.Current?.ValidUntil);
    }

    public async Task<bool> HasFeatureAsync(Feature feature) => (await GetStatusAsync()).Has(feature);

    /// <summary>
    /// Checks a check-in is allowed and how it's paid for. Throws <see cref="LicenseLimitException"/>
    /// when the monthly maximum is reached or there are no credits left.
    /// </summary>
    public async Task<CheckInCharge> AuthorizeCheckInAsync()
    {
        var s = await GetStatusAsync();
        if (s.SubscriptionBlocked)
            throw new LicenseLimitException($"The subscription ended on {s.ValidUntil:d} and the grace period is over. Renew it on Plan & usage to continue.");
        if (s.Unlimited) return CheckInCharge.Included;
        if (s.MaxRemaining <= 0)
            throw new LicenseLimitException($"This month's maximum of {s.Package!.MaxVisitorsPerMonth:N0} check-ins for the {s.Package.Name} plan has been reached.");
        if (s.IncludedRemaining > 0 || s.Package!.CostPerExtraVisitor <= 0) return CheckInCharge.Included;
        if (s.CreditBalance < s.Package.CostPerExtraVisitor)
            throw new LicenseLimitException(
                $"All included check-ins for this month are used and the credit balance ({Format.Money(s.CreditBalance)}) doesn't cover another visitor ({Format.Money(s.Package.CostPerExtraVisitor)}). Buy credit to continue.");
        return CheckInCharge.Credit;
    }

    /// <summary>Charges one check-in beyond the included allowance to the credit balance.</summary>
    public async Task RecordCreditUseAsync(int visitId)
    {
        var cost = (await GetStatusAsync()).Package?.CostPerExtraVisitor ?? 0;
        db.CreditTransactions.Add(new CreditTransaction
        {
            Kind = CreditKind.Usage, Amount = -cost, UnitPrice = cost, VisitId = visitId, Note = "Check-in beyond included visitors"
        });
        await db.SaveChangesAsync();
        _status = null;
    }

    /// <summary>Tops up the credit balance by <paramref name="amount"/> (to be invoiced). Returns an error, or null.</summary>
    /// <summary>Checks a credit purchase amount against the package. Returns an error, or null.</summary>
    public async Task<string?> CheckPurchaseAsync(decimal amount)
    {
        var s = await GetStatusAsync();
        if (s.Package is not { } p) return "No license package is assigned. Contact your provider.";
        amount = Math.Round(amount, 2);
        if (amount < p.MinCreditPurchase) return $"The minimum credit purchase is {Format.Money(p.MinCreditPurchase)}.";
        if (amount > 10_000_000) return "That's more credit than can be bought at once.";
        return null;
    }

    public async Task<string?> BuyCreditsAsync(decimal amount, string by)
    {
        if (await CheckPurchaseAsync(amount) is { } error) return error;
        amount = Math.Round(amount, 2);

        db.CreditTransactions.Add(new CreditTransaction
        {
            Kind = CreditKind.Purchase,
            Amount = amount,
            By = by,
            Note = "To be invoiced"
        });
        await db.SaveChangesAsync();
        _status = null;
        logger.LogInformation("Tenant {Tenant}: {By} bought {Amount} credit", tenant.Slug, by, amount);
        return null;
    }

    public Task<List<CreditTransaction>> HistoryAsync(int take = 50) =>
        db.CreditTransactions.AsNoTracking()
            .Where(t => t.Kind != CreditKind.Usage)
            .OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync();
}
