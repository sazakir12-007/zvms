using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Pages.Feedback;

/// <summary>Staff send feedback about the app to the platform; they see their organisation's feedback and replies.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public const int MaxPerDay = 20;

    [BindProperty]
    public FeedbackCategory Category { get; set; } = FeedbackCategory.Idea;

    [BindProperty, Range(1, 5)]
    public int? Rating { get; set; }

    [BindProperty, Required(ErrorMessage = "Please add a short subject."), StringLength(120)]
    public string Subject { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Please describe your feedback."), StringLength(4000)]
    public string Message { get; set; } = "";

    /// <summary>Page the user came from, so the platform knows what the feedback is about.</summary>
    [BindProperty(SupportsGet = true), StringLength(300)]
    public string? From { get; set; }

    public List<AppFeedback> History { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Enum.IsDefined(Category)) ModelState.AddModelError(nameof(Category), "Choose a type.");
        var since = DateTime.Now.AddDays(-1);
        if (await db.Feedback.CountAsync(f => f.CreatedAt >= since) >= MaxPerDay)
            ModelState.AddModelError(string.Empty, $"Your organisation has sent {MaxPerDay} pieces of feedback in the last 24 hours. Please try again later.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        db.Feedback.Add(new AppFeedback
        {
            Category = Category,
            Rating = Rating,
            Subject = Subject.Trim(),
            Message = Message.Trim(),
            PageUrl = LocalPath(From),
            StaffUserId = StaffAuthService.CurrentUserId(User),
            SubmittedBy = User.Identity?.Name ?? "",
            SubmittedByEmail = User.FindFirstValue(ClaimTypes.Email)
        });
        await db.SaveChangesAsync();
        TempData["Message"] = "Thank you! Your feedback has been sent.";
        return RedirectToPage();
    }

    /// <summary>Keeps only a path within this site (never an outside URL).</summary>
    private static string? LocalPath(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//") ? url[..Math.Min(url.Length, 300)] : null;

    private async Task LoadAsync() =>
        History = await db.Feedback.AsNoTracking().OrderByDescending(f => f.CreatedAt).Take(50).ToListAsync();

    public static string StatusClass(FeedbackStatus s) => s switch
    {
        FeedbackStatus.New => "text-bg-secondary",
        FeedbackStatus.InReview => "text-bg-info",
        FeedbackStatus.Planned => "text-bg-primary",
        FeedbackStatus.Done => "text-bg-success",
        _ => "text-bg-dark"
    };

    public static string StatusLabel(FeedbackStatus s) => s == FeedbackStatus.InReview ? "In review" : s.ToString();
}
