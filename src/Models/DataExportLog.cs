namespace VisitorManagement.Models;

/// <summary>Audit record of a tenant data export: who downloaded what, and when.</summary>
public class DataExportLog : ITenantOwned
{
    public int TenantId { get; set; }

    public int Id { get; set; }

    public int StaffUserId { get; set; }

    public string StaffName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Visit date range, or null for all visits.</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public bool IncludedImages { get; set; }

    public int Visits { get; set; }

    public long Bytes { get; set; }
}
