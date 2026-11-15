using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using VisitorManagement.Services;

using VisitorManagement.Models;

namespace VisitorManagement.Pages.Employees;

/// <summary>Import hosts from CSV / Excel: upload, preview what will happen, then confirm.</summary>
[RequireFeature(Feature.HostImport)]
public class ImportModel(HostImportService importer, IMemoryCache cache, TenantContext tenant) : PageModel
{
    private static readonly TimeSpan HoldUploadFor = TimeSpan.FromMinutes(20);

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public bool UpdateExisting { get; set; } = true;

    [BindProperty]
    public bool CreateDepartments { get; set; } = true;

    /// <summary>Key of the uploaded table held in memory between preview and confirm.</summary>
    [BindProperty]
    public string? UploadKey { get; set; }

    public string? FileName { get; private set; }
    public HostImportPreview? Preview { get; private set; }
    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (Upload is not { Length: > 0 })
        {
            Error = "Choose a CSV or Excel (.xlsx) file to import.";
            return Page();
        }
        if (Upload.Length > TabularFileReader.MaxBytes)
        {
            Error = $"The file is larger than {TabularFileReader.MaxBytes / 1024 / 1024} MB.";
            return Page();
        }

        try
        {
            await using var stream = Upload.OpenReadStream();
            var table = TabularFileReader.Read(stream, Upload.FileName);
            Preview = await importer.PreviewAsync(table, UpdateExisting, CreateDepartments);

            UploadKey = Guid.NewGuid().ToString("N");
            cache.Set(CacheKey(UploadKey), (Upload.FileName, table), HoldUploadFor);
            FileName = Upload.FileName;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            Error = ex is System.Xml.XmlException ? "That Excel file couldn't be read. Try saving it again, or as CSV." : ex.Message;
        }
        return Page();
    }

    /// <summary>Re-checks the held file with the chosen options (e.g. after toggling them) without re-uploading.</summary>
    public async Task<IActionResult> OnPostRecheckAsync() => await WithUploadAsync(async (name, table) =>
    {
        FileName = name;
        Preview = await importer.PreviewAsync(table, UpdateExisting, CreateDepartments);
        return Page();
    });

    public async Task<IActionResult> OnPostApplyAsync() => await WithUploadAsync(async (name, table) =>
    {
        // Validate again at apply time: hosts or departments may have changed since the preview.
        var preview = await importer.PreviewAsync(table, UpdateExisting, CreateDepartments);
        var (added, updated, departments) = await importer.ApplyAsync(preview);
        cache.Remove(CacheKey(UploadKey!));

        var parts = new List<string> { $"{added} host(s) added", $"{updated} updated" };
        if (departments > 0) parts.Add($"{departments} department(s) created");
        var skipped = preview.Count(ImportAction.Skip) + preview.Count(ImportAction.Error);
        if (skipped > 0) parts.Add($"{skipped} row(s) skipped");
        TempData["Message"] = $"Import from {name}: {string.Join(", ", parts)}.";
        return RedirectToPage("./Index");
    });

    public IActionResult OnGetTemplate()
    {
        var bytes = Csv.Build(HostImportService.TemplateHeaders,
        [
            ["Alex Morgan", "alex.morgan@example.com", "Engineering", "+1 555 010 0101"],
            ["Priya Shah", "priya.shah@example.com", "Human Resources", ""],
        ]);
        return File(bytes, "text/csv", "hosts-import-template.csv");
    }

    private async Task<IActionResult> WithUploadAsync(Func<string, List<string[]>, Task<IActionResult>> action)
    {
        if (UploadKey is null || !cache.TryGetValue(CacheKey(UploadKey), out (string Name, List<string[]> Table) upload))
        {
            Error = "The uploaded file has expired. Please upload it again.";
            return Page();
        }
        return await action(upload.Name, upload.Table);
    }

    // Scoped to the signed-in user so one admin can't apply another's upload.
    private string CacheKey(string key) => $"host-import:{tenant.Id}:{StaffAuthService.CurrentUserId(User)}:{key}";
}
