using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>First run only: create the first platform admin. Closed once one exists.</summary>
public class SetupModel(PlatformAuthService auth) : PageModel
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
        await auth.AnyAdminsAsync() ? Redirect("/platform/login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await auth.AnyAdminsAsync()) return Redirect("/platform/login");
        if (!ModelState.IsValid) return Page();

        var (admin, error) = await auth.CreateAsync(Name, Email, Password);
        if (admin is null)
        {
            ModelState.AddModelError(nameof(Password), error!);
            return Page();
        }
        await HttpContext.SignInAsync(PlatformAuthService.Scheme, PlatformAuthService.CreatePrincipal(admin));
        TempData["Message"] = "Platform admin created. Now create your first organisation.";
        return Redirect("/platform/tenants");
    }
}
