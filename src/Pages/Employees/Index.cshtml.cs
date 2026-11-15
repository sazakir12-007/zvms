using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Employees;

public class IndexModel(AppDbContext db, LookupService lookups) : PageModel
{
    [BindProperty]
    public Employee NewEmployee { get; set; } = new();

    public List<Employee> Employees { get; private set; } = new();
    public List<string> Departments { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        NewEmployee.Email = NewEmployee.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(NewEmployee.Department)
            || !(await lookups.ActiveNamesAsync(LookupCategory.Department)).Contains(NewEmployee.Department))
            ModelState.AddModelError("NewEmployee.Department", "Please choose a department.");
        if (await db.Employees.AnyAsync(e => e.Email == NewEmployee.Email))
            ModelState.AddModelError("NewEmployee.Email", "A host with this email already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        db.Employees.Add(NewEmployee);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Added {NewEmployee.Name}.";
        return RedirectToPage();
    }

    // Hosts are deactivated rather than deleted so the visitor history stays intact.
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is null) return NotFound();

        employee.IsActive = !employee.IsActive;
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Employees = await db.Employees.OrderByDescending(e => e.IsActive).ThenBy(e => e.Name).ToListAsync();
        Departments = await lookups.ActiveNamesAsync(LookupCategory.Department);
    }
}
