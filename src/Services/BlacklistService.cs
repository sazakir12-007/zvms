using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

public class VisitorBlacklistedException(BlacklistEntry entry)
    : InvalidOperationException($"Visitor {entry.Name} ({entry.Phone}) is blacklisted.")
{
    public BlacklistEntry Entry { get; } = entry;
}

/// <summary>Blacklisted visitors, matched by mobile number in any format (see PhoneVerification.SameNumber).</summary>
public class BlacklistService(AppDbContext db, ILogger<BlacklistService> logger)
{
    public Task<List<BlacklistEntry>> ActiveAsync() =>
        db.BlacklistEntries.Where(b => b.IsActive).OrderByDescending(b => b.CreatedAt).ToListAsync();

    /// <summary>The active entry for this number, or null.</summary>
    public async Task<BlacklistEntry?> FindAsync(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var entries = await ActiveAsync();
        return entries.FirstOrDefault(b => PhoneVerification.SameNumber(b.Phone, phone));
    }

    /// <summary>Ids of the given visits whose phone number is blacklisted, for flagging them in lists.</summary>
    public async Task<HashSet<int>> BlacklistedVisitIdsAsync(IEnumerable<Visit> visits)
    {
        var entries = await ActiveAsync();
        if (entries.Count == 0) return [];
        return visits
            .Where(v => entries.Any(b => PhoneVerification.SameNumber(b.Phone, v.Phone)))
            .Select(v => v.Id)
            .ToHashSet();
    }

    /// <summary>Adds a number to the blacklist. If it is already listed, updates the reason instead.</summary>
    public async Task<BlacklistEntry> AddAsync(string name, string phone, string reason, int? visitId)
    {
        var normalized = PhoneVerification.Normalize(phone)
            ?? throw new ArgumentException("Not a valid phone number.", nameof(phone));

        var entry = await FindAsync(normalized);
        if (entry is null)
        {
            entry = new BlacklistEntry { Name = name.Trim(), Phone = normalized, Reason = reason.Trim(), VisitId = visitId };
            db.BlacklistEntries.Add(entry);
        }
        else
        {
            entry.Reason = reason.Trim();
            entry.VisitId ??= visitId;
        }

        await db.SaveChangesAsync();
        logger.LogWarning("Blacklisted {Name} ({Phone}): {Reason}", entry.Name, entry.Phone, entry.Reason);
        return entry;
    }

    public async Task<bool> RemoveAsync(int id)
    {
        var entry = await db.BlacklistEntries.FindAsync(id);
        if (entry is null || !entry.IsActive) return false;

        entry.IsActive = false;
        entry.RemovedAt = DateTime.Now;
        await db.SaveChangesAsync();
        logger.LogInformation("Removed {Name} ({Phone}) from the blacklist", entry.Name, entry.Phone);
        return true;
    }
}
