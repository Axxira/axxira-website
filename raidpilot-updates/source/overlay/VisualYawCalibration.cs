namespace RaidPilot.Overlay;

/// <summary>
/// Calibration très prudente du yaw hip-fire à partir de deux capteurs
/// indépendants : Raw Input souris et egomotion visuelle. Aucun échantillon
/// ambigu (déplacement, ADS, free-look, flux peu fiable) n'est autorisé à
/// modifier la calibration. Plusieurs tours purs cohérents sont nécessaires.
/// </summary>
internal sealed class VisualYawCalibration
{
    private const int WindowCapacity = 9;
    private const int MinimumConsistentSamples = 5;
    private readonly Queue<double> _candidates = new(WindowCapacity);

    internal int AcceptedSamples { get; private set; }
    internal int RejectedSamples { get; private set; }
    internal int AppliedUpdates { get; private set; }
    internal string LastDecision { get; private set; } = "vision yaw en attente";

    internal void Reset()
    {
        _candidates.Clear();
        AcceptedSamples = 0;
        RejectedSamples = 0;
        AppliedUpdates = 0;
        LastDecision = "vision yaw nouvelle session";
    }

    internal double? TryGetRealtimeCorrection(
        VisualEgomotionEstimate visual,
        long mouseCounts,
        long mouseMagnitude,
        bool movementActive,
        bool aiming,
        bool freeLook,
        double currentSensitivity)
    {
        if (!double.IsFinite(currentSensitivity)
            || currentSensitivity is < 0.005 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(currentSensitivity));
        }

        if (movementActive
            || aiming
            || freeLook
            || visual.State != VisualEgomotionState.Tracking
            || !visual.IsReliable
            || visual.Confidence < 0.82
            || visual.InlierRatio < 0.78
            || visual.InlierFeatureCount < 9
            || visual.ResidualPixels > 0.65
            || Math.Abs(visual.SceneShiftY) > 0.90
            || mouseCounts == 0
            || mouseMagnitude < 5)
        {
            return null;
        }

        var cancellationRatio = Math.Abs(mouseCounts) / (double)mouseMagnitude;
        var visualYaw = visual.EstimatedYawDeltaDegrees;
        var expectedYaw = mouseCounts * currentSensitivity;
        if (cancellationRatio < 0.93
            || !double.IsFinite(visualYaw)
            || !double.IsFinite(expectedYaw)
            || Math.Abs(visualYaw) is < 0.25 or > 7.0
            || Math.Abs(expectedYaw) is < 0.20 or > 8.0
            || Math.Sign(visualYaw) != Math.Sign(expectedYaw))
        {
            return null;
        }

        var scaleRatio = Math.Abs(visualYaw / expectedYaw);
        if (scaleRatio is < 0.70 or > 1.30)
        {
            return null;
        }

        var innovation = NormalizeSigned180(visualYaw - expectedYaw);
        if (Math.Abs(innovation) > 2.5)
        {
            return null;
        }

        var correction = Math.Clamp(innovation * 0.55, -0.75, 0.75);
        return Math.Abs(correction) >= 0.04
            ? correction
            : null;
    }

    internal double? Observe(
        VisualEgomotionEstimate visual,
        long mouseCounts,
        long mouseMagnitude,
        bool movementActive,
        bool aiming,
        bool freeLook,
        double currentSensitivity)
    {
        if (!double.IsFinite(currentSensitivity)
            || currentSensitivity is < 0.005 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(currentSensitivity));
        }

        if (movementActive || aiming || freeLook)
        {
            LastDecision = "vision yaw ignorée: contexte ambigu";
            return null;
        }

        if (visual.State != VisualEgomotionState.Tracking
            || !visual.IsReliable
            || visual.Confidence < 0.72
            || visual.InlierRatio < 0.68
            || visual.InlierFeatureCount < 7
            || visual.ResidualPixels > 0.85
            || Math.Abs(visual.SceneShiftY) > 1.25)
        {
            LastDecision = "vision yaw ignorée: flux insuffisamment fiable";
            return null;
        }

        var visualYaw = visual.EstimatedYawDeltaDegrees;
        if (!double.IsFinite(visualYaw)
            || Math.Abs(visualYaw) is < 0.20 or > 6.5
            || mouseCounts == 0
            || mouseMagnitude < Math.Abs(mouseCounts)
            || mouseMagnitude < 4)
        {
            LastDecision = "vision yaw ignorée: rotation trop faible ou invalide";
            return null;
        }

        // Une rotation aller-retour dans la même fenêtre n'est pas un segment
        // observable par son seul déplacement final.
        var cancellationRatio = Math.Abs(mouseCounts) / (double)mouseMagnitude;
        if (cancellationRatio < 0.88
            || Math.Sign(visualYaw) != Math.Sign(mouseCounts))
        {
            Reject("vision yaw rejetée: rotation contradictoire");
            return null;
        }

        var candidate = visualYaw / mouseCounts;
        if (!double.IsFinite(candidate)
            || candidate is < 0.005 or > 0.5
            || candidate / currentSensitivity is < 0.65 or > 1.35)
        {
            Reject("vision yaw rejetée: échelle incompatible");
            return null;
        }

        if (_candidates.Count >= 3)
        {
            var median = Median(_candidates);
            var mad = Median(_candidates.Select(value => Math.Abs(value - median)));
            var tolerance = Math.Max(median * 0.08, mad * 4.5);
            if (Math.Abs(candidate - median) > tolerance)
            {
                Reject("vision yaw rejetée: valeur aberrante");
                return null;
            }
        }

        if (_candidates.Count == WindowCapacity)
        {
            _candidates.Dequeue();
        }
        _candidates.Enqueue(candidate);
        AcceptedSamples++;

        if (_candidates.Count < MinimumConsistentSamples)
        {
            LastDecision = $"vision yaw confirmation {_candidates.Count}/{MinimumConsistentSamples}";
            return null;
        }

        var center = Median(_candidates);
        var dispersion = Median(_candidates.Select(value => Math.Abs(value - center)));
        if (dispersion > center * 0.05)
        {
            Reject("vision yaw rejetée: mesures trop dispersées");
            return null;
        }

        var relativeBias = (center / currentSensitivity) - 1.0;
        if (Math.Abs(relativeBias) < 0.002)
        {
            LastDecision = "vision yaw stable";
            _candidates.Clear();
            return null;
        }

        // Maximum 0,5 % par paquet confirmé. Les F8 natifs restent la source
        // absolue et peuvent continuer à corriger plus largement la calibration.
        var correctionFactor = Math.Clamp(
            1.0 + (relativeBias * 0.18),
            0.995,
            1.005);
        var updated = Math.Clamp(
            currentSensitivity * correctionFactor,
            0.005,
            0.5);

        AppliedUpdates++;
        LastDecision = $"vision yaw fusionnée · {currentSensitivity:F5} → {updated:F5}";
        _candidates.Clear();
        return updated;
    }

    private static double NormalizeSigned180(double angle)
    {
        angle %= 360.0;
        if (angle <= -180.0)
        {
            angle += 360.0;
        }
        else if (angle > 180.0)
        {
            angle -= 360.0;
        }
        return angle;
    }

    private void Reject(string reason)
    {
        RejectedSamples++;
        LastDecision = reason;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            return 0;
        }
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) * 0.5;
    }
}