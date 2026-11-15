using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

public class LoginModel(PlatformAuthService auth) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your email."), EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public async Task<IActionResult> OnGetAsync() =>
        await auth.AnyAdminsAsync() ? Page() : Redirect("/platform/setup");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var (admin, error) = await auth.CheckSignInAsync(Email, Password);
        if (admin is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            Password = "";
            return Page();
        }
        await HttpContext.SignInAsync(PlatformAuthService.Scheme, PlatformAuthService.CreatePrincipal(admin));
        return Redirect("/platform/tenants");
    }
}
