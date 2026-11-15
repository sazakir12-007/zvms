namespace VisitorManagement;

/// <param name="IdPrefix">Makes element ids unique when several OTP fields are on one page.</param>
/// <param name="Phone">Number to prefill.</param>
/// <param name="Token">Verification token from an earlier step; when valid the field shows as verified.</param>
/// <param name="AutoSubmit">Submit the surrounding form as soon as the code is verified.</param>
/// <param name="ShowDemoHint">Tell the visitor the dummy code.</param>
public record OtpFieldModel(
    string IdPrefix,
    string? Phone,
    string? Token,
    bool AutoSubmit,
    bool ShowDemoHint,
    string Label = "Mobile number");
