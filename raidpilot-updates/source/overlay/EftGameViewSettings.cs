using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Réglages de caméra EFT lus depuis les fichiers utilisateur. RaidPilot ne
/// modifie jamais ces fichiers; il lit uniquement le FOV vertical choisi par
/// le joueur afin de convertir correctement le flux visuel en angle.
/// </summary>
internal readonly record struct EftGameViewSettings(double VerticalFieldOfViewDegrees)
{
    internal const double DefaultVerticalFieldOfViewDegrees = 50.0;

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Battlestate Games",
        "Escape from Tarkov",
        "Settings",
        "Game.ini");

    internal static EftGameViewSettings Read(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("FieldOfView", out var fieldOfView)
                && fieldOfView.TryGetDouble(out var verticalFov)
                && IsValidVerticalFieldOfView(verticalFov))
            {
                return new EftGameViewSettings(verticalFov);
            }
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
        }

        return new EftGameViewSettings(DefaultVerticalFieldOfViewDegrees);
    }

    internal static bool IsValidVerticalFieldOfView(double degrees) =>
        double.IsFinite(degrees) && degrees is >= 30.0 and <= 120.0;

    internal static double CalculateHorizontalFieldOfViewDegrees(
        double verticalFieldOfViewDegrees,
        double aspectRatio)
    {
        if (!IsValidVerticalFieldOfView(verticalFieldOfViewDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(verticalFieldOfViewDegrees));
        }
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0.1 || aspectRatio > 10.0)
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        var verticalRadians = verticalFieldOfViewDegrees * Math.PI / 180.0;
        return 2.0
            * Math.Atan(Math.Tan(verticalRadians * 0.5) * aspectRatio)
            * 180.0 / Math.PI;
    }

    internal static double CalculateEffectiveHorizontalFieldOfViewDegrees(
        double verticalFieldOfViewDegrees,
        double aspectRatio,
        double horizontalCropFraction)
    {
        if (!double.IsFinite(horizontalCropFraction)
            || horizontalCropFraction is <= 0.05 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalCropFraction));
        }

        var horizontalDegrees = CalculateHorizontalFieldOfViewDegrees(
            verticalFieldOfViewDegrees,
            aspectRatio);
        var horizontalRadians = horizontalDegrees * Math.PI / 180.0;
        return 2.0
            * Math.Atan(Math.Tan(horizontalRadians * 0.5) * horizontalCropFraction)
            * 180.0 / Math.PI;
    }
}
