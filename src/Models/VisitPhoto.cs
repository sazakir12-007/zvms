namespace VisitorManagement.Models;

/// <summary>
/// Webcam photo taken at check-in. Kept in its own table so listing visits
/// never loads image bytes.
/// </summary>
public class VisitPhoto : ITenantOwned
{
    /// <summary>Owning tenant (set automatically on save).</summary>
    public int TenantId { get; set; }

    public int VisitId { get; set; }

    public byte[] Data { get; set; } = [];

    public string ContentType { get; set; } = "image/jpeg";

    public DateTime TakenAt { get; set; } = DateTime.Now;
}
