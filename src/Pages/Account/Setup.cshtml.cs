using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Account;

/// <summary>First run only: create the first admin account. Disabled once any account exists.</summary>
public class SetupModel(StaffAuthService auth, TenantContext tenant) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your name."), StringLength(60), Display(Name = "Your name")]
    public string Name { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter your email."), EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Choose a password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Repeat the password."), DataType(DataType.Password), Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    public async Task<IActionResult> OnGetAsync() =>
        await auth.AnyUsersAsync() ? RedirectToPage("./Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await auth.AnyUsersAsync()) return RedirectToPage("./Login");
        if (StaffAuthService.CheckPasswordRules(Password, Email) is { } rule)
            ModelState.AddModelError(nameof(Password), rule);
        if (!ModelState.IsValid) return Page();

        var (user, error) = await auth.CreateAsync(Name, Email, StaffRoles.Admin, Password, mustChangePassword: false);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffAuthService.CreatePrincipal(user));
        TempData["Message"] = "Welcome! Your admin account is ready. Add other staff under Admin › Staff users.";
        return Redirect(tenant.Home);
    }
}
