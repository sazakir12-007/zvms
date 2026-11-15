using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VisitorManagement.Pages.Account;

/// <summary>Sign out is a POST (with the antiforgery token), so a link elsewhere can't sign staff out.</summary>
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Login");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Message"] = "You've been signed out.";
        return RedirectToPage("./Login");
    }
}
