namespace RaidPilot.Overlay;

public sealed record ScreenshotObservation(
    string FileName,
    double X,
    double Y,
    double Z,
    double QuaternionX,
    double QuaternionY,
    double QuaternionZ,
    double QuaternionW,
    double YawDegrees);
