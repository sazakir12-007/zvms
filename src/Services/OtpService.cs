namespace VisitorManagement.Services;

/// <summary>Sends and checks one-time passcodes for phone verification.</summary>
public interface IOtpService
{
    Task SendAsync(string phone);
    Task<bool> VerifyAsync(string phone, string code);
}

/// <summary>
/// Development stand-in: nothing is sent and the code is always 9999.
/// Replace with an SMS provider (Twilio, Azure Communication Services, ...) that
/// stores a random code per phone with an expiry and an attempt limit.
/// </summary>
public class DummyOtpService(ILogger<DummyOtpService> logger) : IOtpService
{
    public const string Code = "9999";

    public Task SendAsync(string phone)
    {
        logger.LogInformation("[Dummy OTP] Code for {Phone} is {Code}", phone, Code);
        return Task.CompletedTask;
    }

    public Task<bool> VerifyAsync(string phone, string code) => Task.FromResult(code.Trim() == Code);
}
