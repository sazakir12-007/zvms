using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

/// <summary>Platform-wide setting (not tenant data), e.g. the payment gateway configuration.</summary>
public class PlatformSetting
{
    [Key, StringLength(100)]
    public string Key { get; set; } = "";

    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public enum PaymentPurpose
{
    /// <summary>Prepaid credit for check-ins beyond the included ones.</summary>
    Credit,
    /// <summary>Subscription renewal: extends the organisation's "valid until" date.</summary>
    Subscription
}

public enum PaymentStatus
{
    /// <summary>Order created at the gateway; waiting for the payment.</summary>
    Created,
    /// <summary>Payment verified and credit added.</summary>
    Paid,
    Failed
}

/// <summary>An online credit purchase through the payment gateway (Razorpay).</summary>
public class PaymentOrder : ITenantOwned
{
    public int TenantId { get; set; }

    public int Id { get; set; }

    [StringLength(20)]
    public string Gateway { get; set; } = "Razorpay";

    /// <summary>Gateway order id (order_...).</summary>
    [StringLength(60)]
    public string GatewayOrderId { get; set; } = "";

    /// <summary>Gateway payment id (pay_...) once paid.</summary>
    [StringLength(60)]
    public string? GatewayPaymentId { get; set; }

    /// <summary>Credit bought, in rupees.</summary>
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "INR";

    public PaymentStatus Status { get; set; } = PaymentStatus.Created;

    public PaymentPurpose Purpose { get; set; } = PaymentPurpose.Credit;

    /// <summary>Months of subscription bought (renewals only).</summary>
    public int Months { get; set; }

    [StringLength(200)]
    public string? FailureReason { get; set; }

    [StringLength(120)]
    public string By { get; set; } = "";

    /// <summary>Test or live keys were used.</summary>
    public bool TestMode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? PaidAt { get; set; }
}
