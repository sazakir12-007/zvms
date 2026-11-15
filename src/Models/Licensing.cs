using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>Features a license package can switch on. Core check-in, badges, visitor log, hosts,
/// staff and data export are always available and so aren't listed here.</summary>
public enum Feature
{
    Appointments,
    Blacklist,
    BusinessCardScan,
    Reports,
    Analytics,
    HostImport,
    CustomBranding,
    PassTemplates
}

public record FeatureInfo(Feature Feature, string Name, string Description);

public static class Features
{
    public static readonly IReadOnlyList<FeatureInfo> All =
    [
        new(Feature.Appointments, "Appointments", "Book visitors ahead; access codes and phone look-up at the kiosk."),
        new(Feature.Blacklist, "Blacklist", "Block visitors from checking in and from badge printing."),
        new(Feature.BusinessCardScan, "Business card scanning", "Scan a visitor's business card to fill in the check-in form."),
        new(Feature.Reports, "Reports", "Eight report types with CSV export and printing."),
        new(Feature.Analytics, "Dashboard charts", "Visits per day, busiest hours, by purpose and department."),
        new(Feature.HostImport, "Host import", "Import hosts from CSV or Excel."),
        new(Feature.CustomBranding, "Custom branding", "Company logo and banner style."),
        new(Feature.PassTemplates, "Pass templates", "Choose and customise the visitor pass design."),
    ];

    public static FeatureInfo Info(Feature f) => All.First(x => x.Feature == f);

    /// <summary>Initial feature split for the default packages (editable afterwards).</summary>
    public static readonly Feature[] Silver = [Feature.Appointments, Feature.Blacklist];
    public static readonly Feature[] Gold = [.. Silver, Feature.BusinessCardScan, Feature.Reports, Feature.Analytics, Feature.HostImport];
    public static readonly Feature[] Platinum = [.. Gold, Feature.CustomBranding, Feature.PassTemplates];
}

/// <summary>A license package (plan) that organisations are assigned to. Platform-level, not tenant data.</summary>
public class LicensePackage
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string Name { get; set; } = "";

    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>Monthly price, covering <see cref="IncludedVisitors"/> check-ins.</summary>
    [Range(0, 1_000_000)]
    public decimal BasePricePerMonth { get; set; }

    /// <summary>Check-ins per month covered by the base price.</summary>
    [Range(0, 10_000_000)]
    public int IncludedVisitors { get; set; }

    /// <summary>Hard cap on check-ins per month, whatever the credit balance.</summary>
    [Range(1, 10_000_000)]
    public int MaxVisitorsPerMonth { get; set; }

    /// <summary>Charged to the credit balance for each check-in beyond the included ones.</summary>
    [Range(0, 100_000)]
    public decimal CostPerExtraVisitor { get; set; }

    /// <summary>Smallest credit top-up, as an amount of money.</summary>
    [Range(1, 100_000_000)]
    public decimal MinCreditPurchase { get; set; }

    /// <summary>Comma-separated <see cref="Feature"/> names.</summary>
    public string FeatureList { get; set; } = "";

    /// <summary>Inactive packages can't be assigned to new organisations; existing ones keep them.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public HashSet<Feature> FeatureSet
    {
        get => FeatureList.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => Enum.TryParse<Feature>(f, out var x) ? (Feature?)x : null)
            .Where(f => f is not null).Select(f => f!.Value).ToHashSet();
        set => FeatureList = string.Join(',', value.OrderBy(f => f));
    }

    public bool Has(Feature f) => FeatureSet.Contains(f);
}

public enum CreditKind
{
    /// <summary>Credit bought by the organisation (to be invoiced).</summary>
    Purchase,
    /// <summary>One check-in beyond the monthly included visitors, charged at the package's cost per extra visitor.</summary>
    Usage,
    /// <summary>Free credit given by the platform admin.</summary>
    Grant
}

/// <summary>Credit ledger. Credit is money: the balance is the sum of <see cref="Amount"/>.</summary>
public class CreditTransaction : ITenantOwned
{
    public int TenantId { get; set; }

    public int Id { get; set; }

    public CreditKind Kind { get; set; }

    /// <summary>Money added (purchases, grants: positive) or used (extra check-ins: negative).</summary>
    public decimal Amount { get; set; }

    /// <summary>Cost per extra visitor at the time (usage).</summary>
    public decimal UnitPrice { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    public int? VisitId { get; set; }

    [StringLength(120)]
    public string? By { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
