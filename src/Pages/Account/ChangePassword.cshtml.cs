using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Account;

public class ChangePasswordModel(StaffAuthService auth, TenantContext tenant) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Choose a new password."), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Repeat the new password."), DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>The account has a temporary password (new or reset) that must be replaced.</summary>
    public bool Required => User.HasClaim(StaffAuthService.MustChangePasswordClaim, "true");

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var userId = StaffAuthService.CurrentUserId(User)!.Value;
        if (await auth.ChangePasswordAsync(userId, CurrentPassword, NewPassword) is { } error)
        {
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        // The password change rotated the security stamp; re-issue this browser's cookie, keeping
        // "keep me signed in" as it was. (Re-authenticating now would fail on the old stamp.)
        var user = await auth.FindAsync(userId);
        var props = HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffAuthService.CreatePrincipal(user!), props);

        TempData["Message"] = "Password changed. Any other signed-in sessions have been signed out.";
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) && ReturnUrl!.StartsWith(tenant.Home) ? ReturnUrl : tenant.Home);
    }
}
