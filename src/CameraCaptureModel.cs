namespace VisitorManagement;

/// <summary>A webcam capture box that posts a JPEG data URL in a hidden field.</summary>
/// <param name="InputName">Form field the photo is posted as.</param>
/// <param name="Value">Photo from a previous submit, so the visitor doesn't have to retake it.</param>
/// <param name="AutoStart">Start the camera when the page loads.</param>
/// <param name="Mirror">Mirror the live view like a selfie camera (on for faces, off for documents).</param>
public record CameraCaptureModel(
    string Label,
    string InputName,
    string? Value,
    bool AutoStart = true,
    bool Mirror = true,
    string CaptureLabel = "Take photo",
    string? CssClass = null);
