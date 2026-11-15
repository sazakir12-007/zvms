using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Plan;

/// <summary>The organisation's license package, this month's usage and credits. Admins can buy credits.</summary>
public class IndexModel(AppDbContext db, LicenseService license, RazorpayService razorpay, TenantContext tenant) : PageModel
{
    public LicenseStatus Status { get; private set; } = default!;
    public List<CreditTransaction> History { get; private set; } = new();

    /// <summary>Feature the user was sent here for (from a page their plan doesn't include).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Missing { get; set; }

    public FeatureInfo? MissingFeature { get; private set; }

    /// <summary>Cheapest active package that includes <see cref="MissingFeature"/>.</summary>
    public LicensePackage? UpgradeTo { get; private set; }

    /// <summary>Credit to buy, as money.</summary>
    [BindProperty, Display(Name = "Amount")]
    public decimal Amount { get; set; }

    [BindProperty]
    public bool ConfirmInvoice { get; set; }

    public bool IsAdmin => User.IsInRole(StaffRoles.Admin);

    /// <summary>The platform has Razorpay switched on: credit is paid online instead of invoiced.</summary>
    public bool OnlinePayment { get; private set; }
    public bool TestMode { get; private set; }

    /// <summary>Set after "Pay": the page opens Razorpay Checkout for this order.</summary>
    public RazorpayCheckout? Checkout { get; private set; }

    public List<PaymentOrder> Payments { get; private set; } = new();

    /// <summary>Starts an online payment: creates the Razorpay order, then the page opens Checkout.</summary>
    public async Task<IActionResult> OnPostPayAsync()
    {
        if (!IsAdmin) return Forbid();
        ModelState.Remove(nameof(ConfirmInvoice));
        if (await license.CheckPurchaseAsync(Amount) is { } error) ModelState.AddModelError(nameof(Amount), error);
        else
        {
            var (checkout, payError) = await razorpay.CreateOrderAsync(tenant.Current!, Math.Round(Amount, 2),
                User.Identity?.Name ?? "", User.FindFirstValue(ClaimTypes.Email) ?? "");
            if (checkout is null) ModelState.AddModelError(string.Empty, payError!);
            Checkout = checkout;
        }
        await LoadAsync(setDefaultQuantity: false);
        return Page();
    }

    /// <summary>Months to renew the subscription for.</summary>
    [BindProperty]
    public int RenewMonths { get; set; } = 1;

    /// <summary>Renews the subscription: pay base price x months online; the end date moves on when the payment is verified.</summary>
    public async Task<IActionResult> OnPostRenewAsync()
    {
        if (!IsAdmin) return Forbid();
        ModelState.Clear();
        var status = await license.GetStatusAsync();
        if (!Subscription.RenewalMonths.Contains(RenewMonths)) ModelState.AddModelError(nameof(RenewMonths), "Choose a period.");
        else if (status.Package is not { } p) ModelState.AddModelError(nameof(RenewMonths), "No license package is assigned. Contact your provider.");
        else if (!(await razorpay.GetConfigAsync()).Active) ModelState.AddModelError(nameof(RenewMonths), "Online renewal isn't available. Contact your provider to renew.");
        else
        {
            var amount = Math.Round(p.BasePricePerMonth * RenewMonths, 2);
            var (checkout, payError) = await razorpay.CreateOrderAsync(tenant.Current!, amount,
                User.Identity?.Name ?? "", User.FindFirstValue(ClaimTypes.Email) ?? "", PaymentPurpose.Subscription, RenewMonths);
            if (checkout is null) ModelState.AddModelError(nameof(RenewMonths), payError!);
            Checkout = checkout;
        }
        await LoadAsync(setDefaultQuantity: true);
        return Page();
    }

    /// <summary>Razorpay Checkout's success callback: verify the signature, then add the credit or extend the subscription.</summary>
    public async Task<IActionResult> OnPostPaidAsync(string? razorpay_order_id, string? razorpay_payment_id, string? razorpay_signature)
    {
        if (!IsAdmin) return Forbid();
        if (await razorpay.VerifyCheckoutAsync(razorpay_order_id, razorpay_payment_id, razorpay_signature) is { } error)
            TempData["Error"] = error;
        else
            TempData["Message"] = "Payment received. Thank you!";
        return RedirectToPage();
    }

    public async Task OnGetAsync() => await LoadAsync(setDefaultQuantity: true);

    public async Task<IActionResult> OnPostBuyAsync()
    {
        if (!IsAdmin) return Forbid();
        if ((await razorpay.GetConfigAsync()).Active) return RedirectToPage(); // online payment replaces invoicing

        if (!ConfirmInvoice)
            ModelState.AddModelError(nameof(ConfirmInvoice), "Please confirm the purchase will be invoiced.");

        if (ModelState.IsValid)
        {
            var by = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "";
            if (await license.BuyCreditsAsync(Amount, by) is { } error)
            {
                ModelState.AddModelError(nameof(Amount), error);
            }
            else
            {
                TempData["Message"] = $"{Format.Money(Math.Round(Amount, 2))} credit added. It will appear on your next invoice.";
                return RedirectToPage();
            }
        }

        await LoadAsync(setDefaultQuantity: false);
        return Page();
    }

    private async Task LoadAsync(bool setDefaultQuantity)
    {
        Status = await license.GetStatusAsync();
        History = await license.HistoryAsync();
        var config = await razorpay.GetConfigAsync();
        (OnlinePayment, TestMode) = (config.Active, config.TestMode);
        Payments = await db.PaymentOrders.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(10).ToListAsync();
        if (setDefaultQuantity) Amount = Status.Package?.MinCreditPurchase ?? 0;

        if (Enum.TryParse<Feature>(Missing, out var f) && !Status.Has(f))
        {
            MissingFeature = Features.Info(f);
            var packages = await db.LicensePackages.AsNoTracking().Where(p => p.IsActive).ToListAsync();
            UpgradeTo = packages.OrderBy(p => p.BasePricePerMonth).FirstOrDefault(p => p.Has(f));
        }
    }

    /// <summary>Extra check-ins the amount being bought pays for.</summary>
    public int PurchaseCheckIns => Status.Package is { CostPerExtraVisitor: > 0 } p ? (int)Math.Floor(Math.Max(0, Amount) / p.CostPerExtraVisitor) : 0;
}
