using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

public class LogoutModel : PageModel
{
    public IActionResult OnGet() => Redirect("/platform/login");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(PlatformAuthService.Scheme);
        return Redirect("/platform/login");
    }
}
