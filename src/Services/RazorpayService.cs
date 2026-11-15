using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>Razorpay configuration, set by the platform admin. Secrets are stored encrypted (Data Protection).</summary>
public record RazorpayConfig(bool Enabled, string? KeyId, string? KeySecret, string? WebhookSecret, string? BusinessName)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);

    /// <summary>Online payment is offered to organisations only when switched on and configured.</summary>
    public bool Active => Enabled && IsConfigured;

    public bool TestMode => KeyId?.StartsWith("rzp_test_", StringComparison.Ordinal) == true;
}

/// <summary>The checkout the browser opens for a created order.</summary>
public record RazorpayCheckout(string KeyId, string OrderId, long AmountPaise, string Currency, string Name, string Description,
    string PrefillName, string PrefillEmail);

/// <summary>
/// Razorpay payments for credit top-ups: create an order (server), the visitor pays in Razorpay Checkout (browser),
/// then the payment is verified by its signature (browser callback) or by the webhook, whichever arrives first.
/// Credit is added exactly once per order.
/// </summary>
public class RazorpayService(
    AppDbContext db,
    IDataProtectionProvider protection,
    IHttpClientFactory httpFactory,
    TenantProvisioningService provisioning,
    TenantRegistry registry,
    ILogger<RazorpayService> logger)
{
    public const string HttpClientName = "razorpay";
    private const string ApiBase = "https://api.razorpay.com/v1/";

    private static class Keys
    {
        public const string Enabled = "Razorpay.Enabled";
        public const string KeyId = "Razorpay.KeyId";
        public const string KeySecret = "Razorpay.KeySecret";
        public const string WebhookSecret = "Razorpay.WebhookSecret";
        public const string BusinessName = "Razorpay.BusinessName";
    }

    private IDataProtector Protector => protection.CreateProtector("VisitorManagement.PlatformSecrets.v1");

    // ---------- configuration ----------

    public async Task<RazorpayConfig> GetConfigAsync()
    {
        var s = await db.PlatformSettings.AsNoTracking().Where(x => x.Key.StartsWith("Razorpay.")).ToDictionaryAsync(x => x.Key, x => x.Value);
        return new RazorpayConfig(
            s.GetValueOrDefault(Keys.Enabled) == "true",
            s.GetValueOrDefault(Keys.KeyId),
            Unprotect(s.GetValueOrDefault(Keys.KeySecret)),
            Unprotect(s.GetValueOrDefault(Keys.WebhookSecret)),
            s.GetValueOrDefault(Keys.BusinessName));
    }

    /// <summary>Saves the configuration. Null secrets keep the stored ones (the form never shows them).</summary>
    public async Task SaveConfigAsync(bool enabled, string? keyId, string? keySecret, string? webhookSecret, string? businessName)
    {
        await SetAsync(Keys.Enabled, enabled ? "true" : "false");
        await SetAsync(Keys.KeyId, string.IsNullOrWhiteSpace(keyId) ? null : keyId.Trim());
        if (!string.IsNullOrWhiteSpace(keySecret)) await SetAsync(Keys.KeySecret, Protector.Protect(keySecret.Trim()));
        if (!string.IsNullOrWhiteSpace(webhookSecret)) await SetAsync(Keys.WebhookSecret, Protector.Protect(webhookSecret.Trim()));
        await SetAsync(Keys.BusinessName, string.IsNullOrWhiteSpace(businessName) ? null : businessName.Trim());
        await db.SaveChangesAsync();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        await SetAsync(Keys.Enabled, enabled ? "true" : "false");
        await db.SaveChangesAsync();
    }

    public async Task ClearSecretsAsync()
    {
        await SetAsync(Keys.KeySecret, null);
        await SetAsync(Keys.WebhookSecret, null);
        await SetAsync(Keys.Enabled, "false");
        await db.SaveChangesAsync();
    }

    /// <summary>Checks the keys against Razorpay (lists one order). Returns an error, or null.</summary>
    public async Task<string?> TestConnectionAsync()
    {
        var config = await GetConfigAsync();
        if (!config.IsConfigured) return "Enter the key id and key secret first.";
        try
        {
            using var response = await Client(config).GetAsync("orders?count=1");
            if (response.IsSuccessStatusCode) return null;
            return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Razorpay rejected the key id / secret."
                : $"Razorpay answered {(int)response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Couldn't reach Razorpay: " + ex.Message;
        }
    }

    // ---------- payments ----------

    /// <summary>Creates a Razorpay order for a credit top-up by the current tenant. Returns the checkout, or an error.</summary>
    /// <param name="months">Subscription renewals: months bought (purpose Subscription); 0 for credit.</param>
    public async Task<(RazorpayCheckout? Checkout, string? Error)> CreateOrderAsync(Tenant tenant, decimal amount, string byName, string byEmail,
        PaymentPurpose purpose = PaymentPurpose.Credit, int months = 0)
    {
        var config = await GetConfigAsync();
        if (!config.Active) return (null, "Online payment isn't available right now.");

        var paise = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var receipt = $"{tenant.Slug}-{DateTime.UtcNow:yyMMddHHmmss}";
        var body = new
        {
            amount = paise,
            currency = "INR",
            receipt = receipt.Length > 40 ? receipt[..40] : receipt,
            notes = new { tenant = tenant.Slug, purpose = purpose == PaymentPurpose.Subscription ? $"Subscription {months} month(s)" : "Visitor credit top-up" }
        };

        try
        {
            using var response = await Client(config).PostAsJsonAsync("orders", body);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Razorpay order failed for {Tenant}: {Status} {Body}", tenant.Slug, (int)response.StatusCode, json);
                return (null, "The payment couldn't be started. Please try again later.");
            }

            var orderId = JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;
            db.PaymentOrders.Add(new PaymentOrder
            {
                GatewayOrderId = orderId,
                Amount = amount,
                Purpose = purpose,
                Months = months,
                By = string.IsNullOrEmpty(byEmail) ? byName : byEmail,
                TestMode = config.TestMode
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Razorpay order {Order} created for {Tenant}: {Amount}", orderId, tenant.Slug, amount);

            return (new RazorpayCheckout(config.KeyId!, orderId, paise, "INR",
                config.BusinessName ?? "Visitor Management",
                purpose == PaymentPurpose.Subscription ? $"{months}-month subscription for {tenant.Name}" : $"Visitor credit for {tenant.Name}",
                byName, byEmail), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Razorpay unreachable");
            return (null, "Couldn't reach the payment gateway. Please try again later.");
        }
    }

    /// <summary>Verifies the browser's Checkout callback (signature = HMAC-SHA256(order_id|payment_id, key secret)) and adds the credit.</summary>
    public async Task<string?> VerifyCheckoutAsync(string? orderId, string? paymentId, string? signature)
    {
        var config = await GetConfigAsync();
        if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(signature) || !config.IsConfigured)
            return "The payment response was incomplete.";
        if (!SignatureMatches($"{orderId}|{paymentId}", config.KeySecret!, signature))
        {
            logger.LogWarning("Razorpay checkout signature mismatch for order {Order}", orderId);
            return "The payment couldn't be verified. If you were charged, it will be added automatically or refunded.";
        }
        // Only this tenant's orders are visible here (query filter).
        if (!await db.PaymentOrders.AnyAsync(o => o.GatewayOrderId == orderId)) return "Unknown payment.";
        await CompleteAsync(orderId, paymentId, null);
        return null;
    }

    /// <summary>Handles a Razorpay webhook (payment.captured / order.paid). Returns false if the signature is wrong.</summary>
    public async Task<bool> HandleWebhookAsync(string body, string? signature)
    {
        var config = await GetConfigAsync();
        if (string.IsNullOrEmpty(config.WebhookSecret) || string.IsNullOrEmpty(signature)
            || !SignatureMatches(body, config.WebhookSecret, signature))
        {
            logger.LogWarning("Razorpay webhook with a missing or wrong signature ignored");
            return false;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var evt = root.GetProperty("event").GetString();
        if (evt is not ("payment.captured" or "order.paid" or "payment.failed")) return true;

        var payment = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
        var orderId = payment.TryGetProperty("order_id", out var o) ? o.GetString() : null;
        var paymentId = payment.GetProperty("id").GetString();
        if (orderId is null || paymentId is null) return true;

        if (evt == "payment.failed")
        {
            var reason = payment.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            await MarkFailedAsync(orderId, reason);
            return true;
        }

        var paise = payment.TryGetProperty("amount", out var a) ? a.GetInt64() : -1;
        await CompleteAsync(orderId, paymentId, paise);
        return true;
    }

    /// <summary>Marks the order paid and adds the credit, once (a conditional update makes concurrent callbacks safe).</summary>
    private async Task CompleteAsync(string orderId, string paymentId, long? paidPaise)
    {
        var order = await db.PaymentOrders.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.GatewayOrderId == orderId);
        if (order is null) { logger.LogWarning("Razorpay payment for unknown order {Order}", orderId); return; }
        if (paidPaise is { } p && p != (long)Math.Round(order.Amount * 100m, MidpointRounding.AwayFromZero))
        {
            logger.LogError("Razorpay amount mismatch on {Order}: paid {Paid} paise, ordered {Amount}", orderId, p, order.Amount);
            return;
        }
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == order.TenantId);

        await provisioning.AsTenantAsync(tenant, async sp =>
        {
            var tdb = sp.GetRequiredService<AppDbContext>();
            var now = DateTime.Now;
            var claimed = await tdb.PaymentOrders
                .Where(x => x.Id == order.Id && x.Status != PaymentStatus.Paid)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, PaymentStatus.Paid)
                    .SetProperty(x => x.GatewayPaymentId, paymentId)
                    .SetProperty(x => x.PaidAt, now)
                    .SetProperty(x => x.FailureReason, (string?)null));
            if (claimed == 0) return 0; // already credited

            if (order.Purpose == PaymentPurpose.Subscription)
            {
                // Tenants are platform rows (not tenant-owned), so this update isn't tenant-filtered.
                var t = await tdb.Tenants.FirstAsync(x => x.Id == order.TenantId);
                var from = t.ValidUntil;
                t.ValidUntil = Subscription.Extend(t.ValidUntil, order.Months);
                await tdb.SaveChangesAsync();
                registry.Forget(t.Slug);
                logger.LogInformation("Razorpay {Payment}: {Tenant} renewed {Months} month(s), valid until {From:d} -> {Until:d}",
                    paymentId, t.Slug, order.Months, from, t.ValidUntil);
                return 1;
            }

            tdb.CreditTransactions.Add(new CreditTransaction
            {
                Kind = CreditKind.Purchase,
                Amount = order.Amount,
                By = order.By,
                Note = $"Paid online · Razorpay {paymentId}" + (order.TestMode ? " (test)" : "")
            });
            await tdb.SaveChangesAsync();
            logger.LogInformation("Razorpay {Payment} credited {Amount} to {Tenant}", paymentId, order.Amount, tenant.Slug);
            return 1;
        });
    }

    private async Task MarkFailedAsync(string orderId, string? reason)
    {
        await db.PaymentOrders.IgnoreQueryFilters()
            .Where(x => x.GatewayOrderId == orderId && x.Status == PaymentStatus.Created)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, PaymentStatus.Failed)
                .SetProperty(x => x.FailureReason, reason == null ? null : reason.Length > 200 ? reason.Substring(0, 200) : reason));
    }

    // ---------- helpers ----------

    private static bool SignatureMatches(string payload, string secret, string signature)
    {
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    private HttpClient Client(RazorpayConfig config)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(ApiBase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.KeyId}:{config.KeySecret}")));
        return client;
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return Protector.Unprotect(value); }
        catch (CryptographicException) { logger.LogError("A stored Razorpay secret can't be decrypted (Data Protection keys changed). Re-enter it."); return null; }
    }

    private async Task SetAsync(string key, string? value)
    {
        var row = await db.PlatformSettings.FindAsync(key);
        if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value });
        else { row.Value = value; row.UpdatedAt = DateTime.Now; }
    }
}
