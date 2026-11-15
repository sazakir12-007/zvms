using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement;

/// <summary>Everything a pass template needs to draw one visitor pass.</summary>
/// <param name="PhotoUrl">Null when the visit has no photo (initials are shown instead).</param>
/// <param name="Void">Visitor is blacklisted: the pass is shown greyed out with NOT VALID, and never prints.</param>
public record PassModel(Visit Visit, Branding Brand, PassSettings Settings, string? PhotoUrl, bool Void = false)
{
    public string RootClasses =>
        $"pass pass-{Settings.Template.ToString().ToLowerInvariant()} {Format.PurposeClass(Visit.Purpose)} {Settings.OptionClasses}"
        + (Void ? " is-void d-print-none" : "");

    /// <summary>A made-up visit for previews in Admin › Pass templates.</summary>
    public static Visit SampleVisit(string purpose) => new()
    {
        Id = 0,
        VisitorName = "Jordan Sample",
        Company = "Northwind Traders",
        Purpose = purpose,
        Department = "Engineering",
        Employee = new Employee { Name = "Alex Morgan" },
        CheckedInAt = DateTime.Today.AddHours(9).AddMinutes(30),
        ExpectedCheckOutAt = DateTime.Today.AddHours(11),
        BadgeNumber = $"V-{DateTime.Today:yyMMdd}-0042",
        Status = VisitStatus.CheckedIn
    };
}
