using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Admin;

/// <summary>Staff accounts: add, change role, (de)activate, reset password.</summary>
public class UsersModel(AppDbContext db, StaffAuthService auth) : PageModel
{
    public class NewUserInput
    {
        [Required(ErrorMessage = "Enter a name."), StringLength(60)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Enter an email."), EmailAddress, StringLength(200)]
        public string Email { get; set; } = "";

        public string Role { get; set; } = StaffRoles.Reception;

        [Required(ErrorMessage = "Set a temporary password."), Display(Name = "Temporary password")]
        public string Password { get; set; } = "";
    }

    [BindProperty]
    public NewUserInput NewUser { get; set; } = new();

    public List<StaffUser> Users { get; private set; } = new();

    public int CurrentUserId => StaffAuthService.CurrentUserId(User) ?? 0;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (StaffAuthService.CheckPasswordRules(NewUser.Password, NewUser.Email) is { } rule)
            ModelState.AddModelError("NewUser.Password", rule);
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var (user, error) = await auth.CreateAsync(NewUser.Name, NewUser.Email, NewUser.Role, NewUser.Password, mustChangePassword: true);
        if (user is null)
        {
            ModelState.AddModelError("NewUser.Email", error!);
            await LoadAsync();
            return Page();
        }

        TempData["Message"] = $"Added {user.Name}. Give them their temporary password; they'll choose their own when they first sign in.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string name, string role, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) TempData["Error"] = "Name can't be empty.";
        else if (await auth.UpdateAsync(id, name, role, isActive, CurrentUserId) is { } error) TempData["Error"] = error;
        else TempData["Message"] = "Saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(int id, string temporaryPassword)
    {
        if (id == CurrentUserId)
            TempData["Error"] = "To change your own password, use My profile › Change password.";
        else if (await auth.ResetPasswordAsync(id, temporaryPassword) is { } error)
            TempData["Error"] = $"Password not reset: {error}";
        else
            TempData["Message"] = "Password reset. They've been signed out and must choose a new password at next sign-in.";
        return RedirectToPage();
    }

    private async Task LoadAsync() =>
        Users = await db.StaffUsers.OrderByDescending(u => u.IsActive).ThenBy(u => u.Name).ToListAsync();
}
