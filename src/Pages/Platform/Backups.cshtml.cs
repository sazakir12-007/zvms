using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Platform;

/// <summary>Platform admin: manual whole-database backups (every tenant).</summary>
public class BackupsModel(DatabaseBackupService backups, PlatformAuthService auth) : PageModel
{
    public List<BackupFile> Files { get; private set; } = new();
    public string Folder => backups.Directory;
    public int Keep => backups.KeepLatest;

    private int AdminId => StaffAuthService.CurrentUserId(User)!.Value;
    private string AdminName => $"{User.Identity?.Name} <{User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value}>";

    public void OnGet() => Files = backups.List();

    public async Task<IActionResult> OnPostCreateAsync(string? password)
    {
        if (await auth.ConfirmPasswordAsync(AdminId, password) is { } error)
        {
            TempData["Error"] = error;
            return RedirectToPage();
        }
        var (file, createError) = await backups.CreateAsync(AdminName);
        if (file is null) TempData["Error"] = createError;
        else TempData["Message"] = $"Backup {file.Name} created ({file.Bytes / 1024:N0} KB, integrity check passed).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDownloadAsync(string name, string? password)
    {
        if (await auth.ConfirmPasswordAsync(AdminId, password) is { } error)
        {
            TempData["Error"] = error;
            return RedirectToPage();
        }
        if (backups.PathOf(name) is not { } path) return NotFound();
        return PhysicalFile(path, "application/zip", name);
    }

    public IActionResult OnPostDelete(string name)
    {
        if (backups.Delete(name, AdminName)) TempData["Message"] = $"Deleted {name}.";
        else TempData["Error"] = "Backup not found.";
        return RedirectToPage();
    }
}
