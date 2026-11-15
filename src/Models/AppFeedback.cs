using System.ComponentModel.DataAnnotations;

namespace VisitorManagement.Models;

public enum FeedbackCategory
{
    Bug,
    Idea,
    Question,
    Praise,
    Other
}

public enum FeedbackStatus
{
    New,
    InReview,
    Planned,
    Done,
    Closed
}

/// <summary>Feedback about the app sent by an organisation's staff; read and answered by the platform admin.</summary>
public class AppFeedback : ITenantOwned
{
    public int TenantId { get; set; }

    public int Id { get; set; }

    public FeedbackCategory Category { get; set; }

    /// <summary>Overall satisfaction 1–5, optional.</summary>
    [Range(1, 5)]
    public int? Rating { get; set; }

    [Required, StringLength(120)]
    public string Subject { get; set; } = "";

    [Required, StringLength(4000)]
    public string Message { get; set; } = "";

    /// <summary>Page the feedback is about (where the form was opened from).</summary>
    [StringLength(300)]
    public string? PageUrl { get; set; }

    public int? StaffUserId { get; set; }

    [StringLength(120)]
    public string SubmittedBy { get; set; } = "";

    [StringLength(200)]
    public string? SubmittedByEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

    /// <summary>Reply from the platform admin, shown to the organisation.</summary>
    [StringLength(2000)]
    public string? Response { get; set; }

    public DateTime? RespondedAt { get; set; }
}
