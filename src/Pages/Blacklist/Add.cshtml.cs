using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Blacklist;

/// <summary>Blacklist a visitor, either from one of their visits or by entering their details.</summary>
[RequireFeature(Feature.Blacklist)]
public class AddModel(AppDbContext db, BlacklistService blacklist) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? VisitId { get; set; }

    [BindProperty, Required, StringLength(100), Display(Name = "Visitor name")]
    public string Name { get; set; } = "";

    [BindProperty, Required, StringLength(30), Display(Name = "Mobile number")]
    public string Phone { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Please give a reason."), StringLength(500)]
    public string Reason { get; set; } = "";

    /// <summary>The visit this entry is raised from, if any.</summary>
    public Visit? SourceVisit { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (VisitId is { } id)
        {
            SourceVisit = await db.Visits.Include(v => v.Employee).FirstOrDefaultAsync(v => v.Id == id);
            if (SourceVisit is null) return NotFound();
            Name = SourceVisit.VisitorName;
            Phone = SourceVisit.Phone ?? "";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (VisitId is { } id)
            SourceVisit = await db.Visits.Include(v => v.Employee).FirstOrDefaultAsync(v => v.Id == id);

        if (PhoneVerification.Normalize(Phone) is null)
            ModelState.AddModelError(nameof(Phone), "Please enter a valid mobile number.");

        if (!ModelState.IsValid) return Page();

        var entry = await blacklist.AddAsync(Name, Phone, Reason, SourceVisit?.Id);
        TempData["Message"] = $"{entry.Name} ({entry.Phone}) is blacklisted. They can't check in and their badge can't be printed.";
        return RedirectToPage("./Index");
    }
}
