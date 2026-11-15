using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages;

/// <summary>The signed-in staff member's own details.</summary>
public class ProfileModel(StaffAuthService auth) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Please enter a name."), StringLength(60), Display(Name = "Display name")]
    public string Name { get; set; } = "";

    [BindProperty, Phone, StringLength(30)]
    public string? Phone { get; set; }

    public StaffUser Account { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is null) return Challenge();
        (Name, Phone) = (Account.Name, Account.Phone);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadAsync() is null) return Challenge();
        if (!ModelState.IsValid) return Page();

        await auth.UpdateProfileAsync(Account.Id, Name, Phone);

        // Refresh the name shown in the top bar (it comes from the sign-in cookie).
        var user = await auth.FindAsync(Account.Id);
        var props = HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Properties;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffAuthService.CreatePrincipal(user!), props);

        TempData["Message"] = "Profile saved.";
        return RedirectToPage();
    }

    private async Task<StaffUser?> LoadAsync()
    {
        var id = StaffAuthService.CurrentUserId(User);
        Account = (id is null ? null : await auth.FindAsync(id.Value))!;
        return Account;
    }
}
