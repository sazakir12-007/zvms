using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Platform admin: Razorpay settings (on/off, keys) and recent online payments from all organisations.</summary>
public class PaymentsModel(RazorpayService razorpay, AppDbContext db, PlatformAuthService auth) : PageModel
{
    public record Row(PaymentOrder Order, string TenantName);

    public RazorpayConfig Config { get; private set; } = new(false, null, null, null, null);
    public List<Row> Recent { get; private set; } = new();
    public decimal PaidThisMonth { get; private set; }
    public string WebhookUrl => $"{Request.Scheme}://{Request.Host}/payments/razorpay/webhook";

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync(bool enabled, string? keyId, string? keySecret, string? webhookSecret, string? businessName, string? password)
    {
        if (await auth.ConfirmPasswordAsync(StaffAuthService.CurrentUserId(User)!.Value, password) is { } error)
        {
            TempData["Error"] = error;
            return RedirectToPage();
        }
        keyId = keyId?.Trim();
        if (!string.IsNullOrEmpty(keyId) && !(keyId.StartsWith("rzp_test_") || keyId.StartsWith("rzp_live_")))
        {
            TempData["Error"] = "The key id should start with rzp_test_ or rzp_live_.";
            return RedirectToPage();
        }
        await razorpay.SaveConfigAsync(enabled, keyId, keySecret, webhookSecret, businessName);
        var config = await razorpay.GetConfigAsync();
        if (enabled && !config.IsConfigured)
        {
            await razorpay.SetEnabledAsync(false);
            TempData["Error"] = "Saved, but online payment stays off until the key id and key secret are set.";
        }
        else
        {
            TempData["Message"] = config.Active
                ? $"Razorpay is on ({(config.TestMode ? "test" : "live")} mode). Organisations now pay for credit online."
                : "Razorpay is off. Credit purchases are invoiced.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(bool enabled)
    {
        var config = await razorpay.GetConfigAsync();
        if (enabled && !config.IsConfigured)
        {
            TempData["Error"] = "Enter the key id and key secret first.";
            return RedirectToPage();
        }
        await razorpay.SetEnabledAsync(enabled);
        TempData["Message"] = enabled ? "Razorpay switched on." : "Razorpay switched off. Credit purchases are invoiced.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        if (await razorpay.TestConnectionAsync() is { } error) TempData["Error"] = error;
        else TempData["Message"] = "Connected to Razorpay: the keys work.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAsync(string? password)
    {
        if (await auth.ConfirmPasswordAsync(StaffAuthService.CurrentUserId(User)!.Value, password) is { } error)
        {
            TempData["Error"] = error;
            return RedirectToPage();
        }
        await razorpay.ClearSecretsAsync();
        TempData["Message"] = "Razorpay secrets removed and online payment switched off.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Config = await razorpay.GetConfigAsync();
        // Platform view across tenants (aggregate list of payments only).
        var orders = await db.PaymentOrders.IgnoreQueryFilters().AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync();
        var names = await db.Tenants.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        Recent = orders.Select(o => new Row(o, names.GetValueOrDefault(o.TenantId) ?? "(deleted)")).ToList();
        var (start, end) = LicenseService.CurrentPeriod();
        PaidThisMonth = (await db.PaymentOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Status == PaymentStatus.Paid && !o.TestMode && o.PaidAt >= start && o.PaidAt < end)
                .Select(o => o.Amount).ToListAsync()).Sum();
    }

    public static string Mask(string? secret) => string.IsNullOrEmpty(secret) ? "not set" : "saved (hidden)";
}
