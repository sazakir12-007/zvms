using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

public enum ImportAction
{
    Add,
    Update,
    Skip,
    Error
}

/// <param name="Line">Row number in the file (1 = header), for messages.</param>
public record HostImportRow(int Line, string Name, string Email, string? Department, string? Phone, ImportAction Action, List<string> Problems);

public record HostImportPreview(
    List<HostImportRow> Rows,
    List<string> MissingDepartments,
    List<string> UnrecognisedColumns)
{
    public int Count(ImportAction a) => Rows.Count(r => r.Action == a);
}

/// <summary>Imports hosts from a spreadsheet: preview first, then apply.</summary>
public class HostImportService(AppDbContext db, LookupService lookups)
{
    public static readonly string[] TemplateHeaders = ["Name", "Email", "Department", "Phone"];

    // Accepted spellings for each column (compared without spaces, dashes or case).
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["Name"] = ["name", "fullname", "host", "hostname", "employee", "employeename", "staffname"],
        ["Email"] = ["email", "emailaddress", "mail", "workemail"],
        ["Department"] = ["department", "dept", "team", "division"],
        ["Phone"] = ["phone", "mobile", "telephone", "tel", "phonenumber", "mobilenumber", "extension"],
    };

    /// <summary>Validates every row and works out what would happen, without saving anything.</summary>
    public async Task<HostImportPreview> PreviewAsync(List<string[]> table, bool updateExisting, bool createDepartments)
    {
        if (table.Count == 0) throw new InvalidDataException("The file is empty.");

        var header = table[0];
        var map = new Dictionary<string, int>();
        var unrecognised = new List<string>();
        for (var i = 0; i < header.Length; i++)
        {
            var key = Normalize(header[i]);
            var field = Aliases.FirstOrDefault(a => a.Value.Contains(key)).Key;
            if (field is not null && !map.ContainsKey(field)) map[field] = i;
            else if (!string.IsNullOrWhiteSpace(header[i])) unrecognised.Add(header[i]);
        }
        if (!map.ContainsKey("Name") || !map.ContainsKey("Email"))
            throw new InvalidDataException("The first row must be column headings including Name and Email. Download the template to see the layout.");

        string? Cell(string[] row, string field) =>
            map.TryGetValue(field, out var i) && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;

        var existing = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Email.ToLower());
        var departments = (await lookups.AllAsync(LookupCategory.Department))
            .ToDictionary(d => d.Name, d => d.Name, StringComparer.OrdinalIgnoreCase);
        var missingDepartments = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEmails = new HashSet<string>();
        var emailCheck = new EmailAddressAttribute();
        var phoneCheck = new PhoneAttribute();
        var rows = new List<HostImportRow>();

        for (var r = 1; r < table.Count; r++)
        {
            var row = table[r];
            var problems = new List<string>();
            var name = Cell(row, "Name") ?? "";
            var email = (Cell(row, "Email") ?? "").ToLowerInvariant();
            var department = Cell(row, "Department");
            var phone = Cell(row, "Phone");

            if (name.Length == 0) problems.Add("Name is missing");
            else if (name.Length > 100) problems.Add("Name is too long");
            if (email.Length == 0) problems.Add("Email is missing");
            else if (!emailCheck.IsValid(email) || email.Length > 200) problems.Add("Email isn't valid");
            else if (!seenEmails.Add(email)) problems.Add("Same email appears earlier in the file");
            if (phone is not null && (!phoneCheck.IsValid(phone) || phone.Length > 30)) problems.Add("Phone isn't valid");

            // Match the department list's spelling; unknown ones are created or flagged.
            if (department is not null)
            {
                if (departments.TryGetValue(department, out var canonical)) department = canonical;
                else if (department.Length > 50) problems.Add("Department name is too long");
                else if (createDepartments) missingDepartments.Add(department);
                else problems.Add($"Department \"{department}\" isn't in the list");
            }

            var action = problems.Count > 0 ? ImportAction.Error
                : existing.ContainsKey(email) ? (updateExisting ? ImportAction.Update : ImportAction.Skip)
                : ImportAction.Add;
            rows.Add(new HostImportRow(r + 1, name, email, department, phone, action, problems));
        }

        return new HostImportPreview(rows, missingDepartments.ToList(), unrecognised);
    }

    /// <summary>Applies a preview: adds/updates hosts and creates missing departments, all in one transaction.</summary>
    public async Task<(int Added, int Updated, int Departments)> ApplyAsync(HostImportPreview preview)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var departmentsCreated = 0;
        foreach (var name in preview.MissingDepartments)
        {
            // Only departments a row actually uses.
            if (!preview.Rows.Any(r => r.Action is ImportAction.Add or ImportAction.Update
                                       && string.Equals(r.Department, name, StringComparison.OrdinalIgnoreCase))) continue;
            if (await lookups.AddAsync(LookupCategory.Department, name, null, null) is null) departmentsCreated++;
        }

        var emails = preview.Rows.Where(r => r.Action == ImportAction.Update).Select(r => r.Email).ToList();
        var toUpdate = await db.Employees.Where(e => emails.Contains(e.Email.ToLower())).ToDictionaryAsync(e => e.Email.ToLower());

        int added = 0, updated = 0;
        foreach (var row in preview.Rows)
        {
            if (row.Action == ImportAction.Add)
            {
                db.Employees.Add(new Employee { Name = row.Name, Email = row.Email, Department = row.Department, Phone = row.Phone });
                added++;
            }
            else if (row.Action == ImportAction.Update && toUpdate.TryGetValue(row.Email, out var e))
            {
                e.Name = row.Name;
                if (row.Department is not null) e.Department = row.Department;
                if (row.Phone is not null) e.Phone = row.Phone;
                e.IsActive = true;
                updated++;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (added, updated, departmentsCreated);
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
