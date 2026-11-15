using VisitorManagement.Models;

namespace VisitorManagement;

/// <summary>Where a visit card is shown, which decides the actions on it.</summary>
public enum VisitCardContext
{
    OnSite,
    Expected,
    Log
}

/// <param name="Blacklisted">The visitor's phone number is on the blacklist.</param>
public record VisitCardModel(Visit Visit, VisitCardContext Context, bool Blacklisted = false);
