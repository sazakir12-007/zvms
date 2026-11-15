using Microsoft.EntityFrameworkCore;
using VisitorManagement.Data;
using VisitorManagement.Models;

namespace VisitorManagement.Services;

/// <summary>Visit lifecycle rules: appointments, check-in, check-out, badges and access codes.</summary>
public class VisitService(AppDbContext db, BlacklistService blacklist, LicenseService license, ILogger<VisitService> logger)
{
    // No 0/O/1/I so codes are easy to read aloud and type.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<Visit> CreateAppointmentAsync(Visit visit, int stayMinutes)
    {
        visit.Status = VisitStatus.Expected;
        visit.AccessCode = await NewAccessCodeAsync();
        visit.ExpectedCheckOutAt = visit.ExpectedAt?.AddMinutes(stayMinutes);
        await SetDepartmentAsync(visit);
        db.Visits.Add(visit);
        await db.SaveChangesAsync();
        await NotifyHostAsync(visit, $"{visit.VisitorName} has an appointment at {visit.ExpectedAt:g}. Access code {visit.AccessCode}.");
        return visit;
    }

    /// <summary>Checks in a walk-in visitor whose phone has been verified.</summary>
    /// <exception cref="VisitorBlacklistedException">The phone number is blacklisted.</exception>
    /// <exception cref="LicenseLimitException">The plan's check-in allowance is used up.</exception>
    public async Task<Visit> CheckInWalkInAsync(Visit visit, CheckInCapture capture, int stayMinutes)
    {
        await EnsureNotBlacklistedAsync(visit.Phone);
        var charge = await license.AuthorizeCheckInAsync();
        MarkCheckedIn(visit, stayMinutes);
        await SetDepartmentAsync(visit);
        visit.PhoneVerified = true;
        visit.AgreementAccepted = true;
        Apply(visit, capture);
        db.Visits.Add(visit);
        await db.SaveChangesAsync(); // assigns Id, needed for the badge number and attachments
        visit.BadgeNumber = BadgeFor(visit);
        AddAttachments(visit.Id, capture);
        await db.SaveChangesAsync();
        if (charge == CheckInCharge.Credit) await license.RecordCreditUseAsync(visit.Id);
        await NotifyHostAsync(visit, $"{visit.VisitorName} has arrived and is waiting at reception.");
        return visit;
    }

    /// <summary>Checks in an appointment. Returns false if it is not in the Expected state.</summary>
    /// <exception cref="VisitorBlacklistedException">The phone number is blacklisted.</exception>
    /// <exception cref="LicenseLimitException">The plan's check-in allowance is used up.</exception>
    public async Task<bool> CheckInAsync(int id, string verifiedPhone, CheckInCapture capture, int stayMinutes)
    {
        var visit = await db.Visits.Include(v => v.Photo).FirstOrDefaultAsync(v => v.Id == id);
        if (visit is null || visit.Status != VisitStatus.Expected) return false;

        await EnsureNotBlacklistedAsync(verifiedPhone);
        var charge = await license.AuthorizeCheckInAsync();
        MarkCheckedIn(visit, stayMinutes);
        await SetDepartmentAsync(visit);
        visit.Phone = verifiedPhone;
        visit.PhoneVerified = true;
        visit.AgreementAccepted = true;
        Apply(visit, capture);
        visit.BadgeNumber = BadgeFor(visit);
        AddAttachments(visit.Id, capture);
        await db.SaveChangesAsync();
        if (charge == CheckInCharge.Credit) await license.RecordCreditUseAsync(visit.Id);
        await NotifyHostAsync(visit, $"{visit.VisitorName} has arrived and is waiting at reception.");
        return true;
    }

    private static void Apply(Visit visit, CheckInCapture capture)
    {
        if (capture.Photo is { Length: > 0 } photo) visit.Photo = new VisitPhoto { Data = photo };
        visit.Building = capture.Building;
        visit.Floor = capture.Floor;
    }

    private void AddAttachments(int visitId, CheckInCapture capture)
    {
        if (capture.Signature is { Length: > 0 } signature)
        {
            db.VisitAttachments.Add(new VisitAttachment
            {
                VisitId = visitId, Kind = AttachmentKind.Signature, Data = signature, ContentType = "image/png"
            });
        }
        if (capture.BusinessCardImage is { Length: > 0 } card)
        {
            db.VisitAttachments.Add(new VisitAttachment
            {
                VisitId = visitId, Kind = AttachmentKind.BusinessCard, Data = card, ContentType = "image/jpeg"
            });
        }
    }

    public Task<Visit?> FindExpectedByCodeAsync(string code)
    {
        code = code.Trim().ToUpperInvariant();
        return db.Visits.Include(v => v.Employee)
            .FirstOrDefaultAsync(v => v.AccessCode == code && v.Status == VisitStatus.Expected);
    }

    /// <summary>
    /// Appointment booked under this (already verified) number. If there are several,
    /// returns the one expected closest to now.
    /// </summary>
    public async Task<Visit?> FindExpectedByPhoneAsync(string phone)
    {
        var candidates = await db.Visits.Include(v => v.Employee)
            .Where(v => v.Status == VisitStatus.Expected && v.Phone != null)
            .ToListAsync();

        var now = DateTime.Now;
        return candidates
            .Where(v => PhoneVerification.SameNumber(v.Phone, phone))
            .OrderBy(v => Math.Abs(((v.ExpectedAt ?? now) - now).Ticks))
            .FirstOrDefault();
    }

    public async Task<bool> CheckOutAsync(int id)
    {
        var visit = await db.Visits.FindAsync(id);
        if (visit is null || visit.Status != VisitStatus.CheckedIn) return false;

        visit.Status = VisitStatus.CheckedOut;
        visit.CheckedOutAt = DateTime.Now;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CancelAsync(int id)
    {
        var visit = await db.Visits.FindAsync(id);
        if (visit is null || visit.Status != VisitStatus.Expected) return false;

        visit.Status = VisitStatus.Cancelled;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Checks out everyone still on site, e.g. at end of day.</summary>
    public Task<int> CheckOutAllAsync()
    {
        var now = DateTime.Now;
        return db.Visits.Where(v => v.Status == VisitStatus.CheckedIn)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.Status, VisitStatus.CheckedOut)
                .SetProperty(v => v.CheckedOutAt, now));
    }

    /// <summary>Planned length of an appointment in minutes, used to prefill the expected stay at check-in.</summary>
    public static int? PlannedStayMinutes(Visit visit) =>
        visit.ExpectedAt is { } from && visit.ExpectedCheckOutAt is { } to ? (int)(to - from).TotalMinutes : null;

    private static void MarkCheckedIn(Visit visit, int stayMinutes)
    {
        visit.Status = VisitStatus.CheckedIn;
        visit.CheckedInAt = DateTime.Now;
        visit.ExpectedCheckOutAt = visit.CheckedInAt.Value.AddMinutes(stayMinutes);
    }

    private async Task SetDepartmentAsync(Visit visit)
    {
        var host = visit.Employee ?? await db.Employees.FindAsync(visit.EmployeeId);
        visit.Department = host?.Department;
    }

    // Backstop: pages check the blacklist first so they can show a friendly message.
    private async Task EnsureNotBlacklistedAsync(string? phone)
    {
        if (await blacklist.FindAsync(phone) is { } entry)
        {
            logger.LogWarning("Blocked check-in for blacklisted visitor {Name} ({Phone})", entry.Name, entry.Phone);
            throw new VisitorBlacklistedException(entry);
        }
    }

    private static string BadgeFor(Visit visit) => $"V-{visit.CheckedInAt:yyMMdd}-{visit.Id:D4}";

    private async Task<string> NewAccessCodeAsync()
    {
        while (true)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
            var code = new string(chars);

            if (!await db.Visits.AnyAsync(v => v.AccessCode == code && v.Status == VisitStatus.Expected))
                return code;
        }
    }

    // Hook point for email / Teams / Slack notifications. Logs for now.
    private async Task NotifyHostAsync(Visit visit, string message)
    {
        var host = visit.Employee ?? await db.Employees.FindAsync(visit.EmployeeId);
        logger.LogInformation("Notify host {Host} <{Email}>: {Message}", host?.Name, host?.Email, message);
    }
}

/// <summary>Everything captured at the kiosk besides the form fields.</summary>
/// <param name="Photo">Webcam photo (JPEG).</param>
/// <param name="Signature">Signature accepting the visitor agreement (PNG).</param>
/// <param name="BusinessCardImage">Optional photo of the visitor's business card (JPEG).</param>
public record CheckInCapture(
    byte[]? Photo,
    byte[]? Signature,
    byte[]? BusinessCardImage,
    string? Building = null,
    string? Floor = null);
