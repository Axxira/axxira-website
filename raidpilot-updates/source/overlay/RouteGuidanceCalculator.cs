namespace RaidPilot.Overlay;

/// <summary>
/// Résultat d'un guidage dans le plan horizontal de Tarkov.
/// <para>
/// La flèche est pensée comme une flèche dessinée vers le haut : une rotation
/// positive la tourne dans le sens horaire (droite), une rotation négative dans
/// le sens antihoraire (gauche).
/// </para>
/// </summary>
public readonly record struct RouteGuidance(
    double DistanceMeters,
    double HorizontalDistanceMeters,
    double VerticalDeltaMeters,
    double TargetBearingDegrees,
    double ArrowRotationDegrees,
    bool HasDirection,
    bool HasArrived);

/// <summary>
/// Transforme les coordonnées monde X/Y/Z et le yaw Unity du joueur en une
/// distance et une rotation de flèche vers une destination.
/// </summary>
public static class RouteGuidanceCalculator
{
    private const double RadiansToDegrees = 180.0 / Math.PI;

    /// <summary>
    /// Calcule le guidage. Dans le repère Unity utilisé par Tarkov, 0° regarde
    /// vers +Z et 90° vers +X.
    /// </summary>
    /// <remarks>
    /// <paramref name="arrivalRadiusMeters"/> évite qu'une flèche instable
    /// tourne autour du joueur lorsqu'il est déjà à l'extraction.
    /// </remarks>
    public static bool TryCalculate(
        double playerX,
        double playerY,
        double playerZ,
        double playerYawDegrees,
        double targetX,
        double targetY,
        double targetZ,
        out RouteGuidance guidance,
        double arrivalRadiusMeters = 5.0,
        double arrivalVerticalToleranceMeters = 3.0)
    {
        guidance = default;
        if (!AreFinite(playerX, playerY, playerZ, playerYawDegrees, targetX, targetY, targetZ)
            || !double.IsFinite(arrivalRadiusMeters)
            || arrivalRadiusMeters < 0.0
            || !double.IsFinite(arrivalVerticalToleranceMeters)
            || arrivalVerticalToleranceMeters < 0.0)
        {
            return false;
        }

        var deltaX = targetX - playerX;
        var deltaY = targetY - playerY;
        var deltaZ = targetZ - playerZ;
        var horizontalDistance = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        var distance = Math.Sqrt((horizontalDistance * horizontalDistance) + (deltaY * deltaY));
        if (!double.IsFinite(horizontalDistance) || !double.IsFinite(distance))
        {
            return false;
        }

        if (horizontalDistance <= arrivalRadiusMeters
            && Math.Abs(deltaY) <= arrivalVerticalToleranceMeters)
        {
            guidance = new RouteGuidance(
                DistanceMeters: distance,
                HorizontalDistanceMeters: horizontalDistance,
                VerticalDeltaMeters: deltaY,
                TargetBearingDegrees: Normalize360(playerYawDegrees),
                ArrowRotationDegrees: 0.0,
                HasDirection: false,
                HasArrived: true);
            return true;
        }

        // atan2(X, Z) correspond au yaw Unity : nord/+Z = 0°, est/+X = 90°.
        var targetBearing = Normalize360(Math.Atan2(deltaX, deltaZ) * RadiansToDegrees);
        var relativeBearing = NormalizeSigned180(targetBearing - Normalize360(playerYawDegrees));

        guidance = new RouteGuidance(
            DistanceMeters: distance,
            HorizontalDistanceMeters: horizontalDistance,
            VerticalDeltaMeters: deltaY,
            TargetBearingDegrees: targetBearing,
            ArrowRotationDegrees: relativeBearing,
            HasDirection: true,
            HasArrived: false);
        return true;
    }

    private static bool AreFinite(params double[] values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static double Normalize360(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeSigned180(double angleDegrees)
    {
        var normalized = (angleDegrees + 180.0) % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }

        return normalized - 180.0;
    }
}
