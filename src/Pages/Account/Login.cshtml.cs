using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Account;

public class LoginModel(StaffAuthService auth, TenantContext tenant) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your email."), EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty, Display(Name = "Keep me signed in on this computer")]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // First run: there are no accounts yet, so create the first admin.
        if (!await auth.AnyUsersAsync()) return RedirectToPage("./Setup");
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturnUrl());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var (user, error) = await auth.CheckSignInAsync(Email, Password);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            Password = "";
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            StaffAuthService.CreatePrincipal(user),
            new AuthenticationProperties
            {
                IsPersistent = RememberMe,
                ExpiresUtc = RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
            });

        return user.MustChangePassword
            ? RedirectToPage("./ChangePassword", new { returnUrl = SafeReturnUrl() })
            : LocalRedirect(SafeReturnUrl());
    }

    // Only ever return to a page of this tenant.
    private string SafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) && (ReturnUrl! + "/").StartsWith(tenant.Home, StringComparison.Ordinal) ? ReturnUrl : tenant.Home;
}
