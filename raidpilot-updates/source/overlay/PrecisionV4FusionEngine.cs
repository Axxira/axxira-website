using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// The map has no authoritative wall mesh yet. Only explicitly verified walkable
/// polygons may constrain a visual correction; absent data MUST remain neutral.
/// Vertical floor inconsistencies are always rejected.
/// </summary>
internal readonly record struct PrecisionV4Point(double X, double Z);

internal sealed record PrecisionV4WalkableArea(
    string MapId, double MinimumY, double MaximumY,
    IReadOnlyList<PrecisionV4Point> Outline);

internal sealed class PrecisionV4GeometryGuard
{
    private readonly IReadOnlyList<PrecisionV4WalkableArea> _verifiedAreas;

    internal PrecisionV4GeometryGuard(
        IReadOnlyList<PrecisionV4WalkableArea>? verifiedAreas = null)
    {
        _verifiedAreas = verifiedAreas ?? Array.Empty<PrecisionV4WalkableArea>();
        foreach (var area in _verifiedAreas)
        {
            if (string.IsNullOrWhiteSpace(area.MapId)
                || !double.IsFinite(area.MinimumY)
                || !double.IsFinite(area.MaximumY)
                || area.MinimumY >= area.MaximumY
                || area.Outline is null || area.Outline.Count < 3
                || area.Outline.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Z)))
            {
                throw new ArgumentException("La géométrie vérifiée est invalide.", nameof(verifiedAreas));
            }
        }
    }

    internal int VerifiedAreaCount => _verifiedAreas.Count;

    internal bool Allows(string mapId, RealtimePose estimate, VisualKeyframePose target)
    {
        if (Math.Abs(estimate.Y - target.Y) > 2.25)
        {
            return false; // Do not snap between floors based on a flat projection.
        }

        var matching = _verifiedAreas.Where(area =>
            string.Equals(area.MapId, mapId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length == 0)
        {
            return true; // Unknown terrain must not be portrayed as surveyed walls.
        }

        foreach (var area in matching)
        {
            if (estimate.Y < area.MinimumY || estimate.Y >= area.MaximumY
                || target.Y < area.MinimumY || target.Y >= area.MaximumY)
            {
                continue;
            }
            if (SegmentInside(area.Outline, estimate.X, estimate.Z, target.X, target.Z))
            {
                return true;
            }
        }
        return false;
    }

    private static bool SegmentInside(IReadOnlyList<PrecisionV4Point> polygon,
        double ax, double az, double bx, double bz)
    {
        var distance = Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(az - bz, 2));
        // Bound CPU work and reject unverified long jumps instead of skipping cells.
        var steps = (int)Math.Ceiling(distance / 0.5);
        if (steps > 512)
        {
            return false;
        }
        steps = Math.Max(1, steps);
        for (var step = 0; step <= steps; step++)
        {
            var t = step / (double)steps;
            if (!Contains(polygon, ax + (bx - ax) * t, az + (bz - az) * t))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool Contains(IReadOnlyList<PrecisionV4Point> polygon, double x, double z)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var cross = (x - a.X) * (b.Z - a.Z) - (z - a.Z) * (b.X - a.X);
            if (Math.Abs(cross) < 1e-8
                && x >= Math.Min(a.X, b.X) - 1e-8 && x <= Math.Max(a.X, b.X) + 1e-8
                && z >= Math.Min(a.Z, b.Z) - 1e-8 && z <= Math.Max(a.Z, b.Z) + 1e-8)
            {
                return true;
            }
            if ((a.Z > z) != (b.Z > z)
                && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// Conservative covariance-inspired innovation gate. It never invents an EFT
/// position and never upgrades an uncertain visual match to an exact F8 anchor.
/// Existing V3 remains available by disabling the optional V4 mode.
/// </summary>
internal sealed class PrecisionV4FusionEngine
{
    private readonly PrecisionV4GeometryGuard _geometry;
    private long _accepted, _rejected, _limitedHard;
    private string _lastDecision = "en attente de repères";

    internal PrecisionV4FusionEngine(PrecisionV4GeometryGuard? geometry = null)
    {
        _geometry = geometry ?? new PrecisionV4GeometryGuard();
    }

    internal long Accepted => _accepted;
    internal long Rejected => _rejected;
    internal long LimitedHardSnaps => _limitedHard;
    internal string Diagnostic =>
        $"repères appliqués {_accepted}, candidats refusés {_rejected}, "
        + $"snaps fortement bornés {_limitedHard}, zones navigables vérifiées {_geometry.VerifiedAreaCount}; "
        + _lastDecision;

    internal void Reset()
    {
        _accepted = _rejected = _limitedHard = 0;
        _lastDecision = "en attente de repères";
    }

    internal bool TryFuse(
        string mapId,
        VisualRelocalizationDecision candidate,
        RealtimePose estimatedPoseAtCapture,
        bool captureProvenLive,
        long capturedAt,
        out VisualRelocalizationDecision refined)
    {
        refined = candidate;
        if (!captureProvenLive || capturedAt <= 0
            || string.IsNullOrWhiteSpace(mapId)
            || !candidate.ShouldCorrect || candidate.TargetPose is not { } target
            || !new[] { estimatedPoseAtCapture.X, estimatedPoseAtCapture.Y,
                estimatedPoseAtCapture.Z, estimatedPoseAtCapture.YawDegrees,
                estimatedPoseAtCapture.PositionUncertaintyMeters,
                estimatedPoseAtCapture.YawUncertaintyDegrees,
                target.X, target.Y, target.Z, target.YawDegrees, candidate.Confidence,
                candidate.PositionResidualMeters, candidate.YawResidualDegrees }.All(double.IsFinite)
            || estimatedPoseAtCapture.PositionUncertaintyMeters < 0
            || estimatedPoseAtCapture.YawUncertaintyDegrees < 0
            || candidate.Confidence is < 0 or > 1)
        {
            return Refuse("preuve visuelle absente ou périmée");
        }

        if (!_geometry.Allows(mapId, estimatedPoseAtCapture, target))
        {
            return Refuse("étage ou géométrie vérifiée incompatible");
        }

        var dx = target.X - estimatedPoseAtCapture.X;
        var dz = target.Z - estimatedPoseAtCapture.Z;
        var innovation = Math.Sqrt(dx * dx + dz * dz);
        var modelSigma = Math.Max(0.5, estimatedPoseAtCapture.PositionUncertaintyMeters);
        var sourceNoise = candidate.Source switch
        {
            VisualRelocalizationSource.Consensus => 0.45,
            VisualRelocalizationSource.SuperKeyframe => 0.85,
            _ => 1.45,
        };
        var measurementSigma = sourceNoise + (1.0 - candidate.Confidence) * 4.0;
        var combinedSigma = Math.Sqrt(modelSigma * modelSigma
            + measurementSigma * measurementSigma);

        // Temporal cross-frame confirmation is already performed by the
        // recognizers (3/5 consecutive matches). V4 adds a strict independent
        // recognizer or proven global-loop requirement for a large snap.
        if (candidate.Mode == VisualCorrectionMode.HardSnap)
        {
            var strongProof = candidate.Confidence >= 0.94
                && (candidate.Source == VisualRelocalizationSource.Consensus
                    || (candidate.GlobalLoopClosure
                        && candidate.Source == VisualRelocalizationSource.SuperKeyframe
                        && candidate.Confidence >= 0.96));
            if (!strongProof)
            {
                return Refuse("grand recalage sans double confirmation forte");
            }
            if (innovation > Math.Max(12.0, 5.0 * modelSigma)
                || (modelSigma < 2.0 && innovation > 5.0))
            {
                return Refuse("saut incompatible avec l'incertitude et la carte");
            }
        }
        else if (innovation > Math.Max(2.0, 4.0 * combinedSigma))
        {
            return Refuse("innovation visuelle statistiquement aberrante");
        }

        var posVariance = modelSigma * modelSigma;
        var yawSigma = Math.Max(1.0, estimatedPoseAtCapture.YawUncertaintyDegrees);
        var yawNoise = 2.0 + (1.0 - candidate.Confidence) * 15.0;
        var positionGain = Math.Min(candidate.PositionGain,
            posVariance / (posVariance + measurementSigma * measurementSigma));
        var yawGain = Math.Min(candidate.YawGain,
            yawSigma * yawSigma / (yawSigma * yawSigma + yawNoise * yawNoise));
        // Never promote an untrusted weak match into a stronger correction.
        var maximumTranslation = candidate.MaximumTranslationStepMeters;
        var maximumYaw = candidate.MaximumYawStepDegrees;
        if (candidate.Mode == VisualCorrectionMode.HardSnap)
        {
            maximumTranslation = Math.Min(maximumTranslation,
                Math.Clamp(modelSigma * 1.5, 2.0, 8.0));
            maximumYaw = Math.Min(maximumYaw, 25.0);
            _limitedHard++;
        }
        if (positionGain < 0.025 && yawGain < 0.025)
        {
            return Refuse("estimateur déjà plus fiable que la mesure");
        }
        refined = candidate with
        {
            PositionGain = Math.Clamp(positionGain, 0.0, 1.0),
            YawGain = Math.Clamp(yawGain, 0.0, 1.0),
            MaximumTranslationStepMeters = maximumTranslation,
            MaximumYawStepDegrees = maximumYaw,
        };
        _accepted++;
        _lastDecision = $"fusion {candidate.Mode} : confiance {candidate.Confidence:F2}, "
            + $"résidu {innovation:F1} m, gain {positionGain:F2}";
        return true;
    }

    private bool Refuse(string reason)
    {
        _rejected++;
        _lastDecision = "rejet : " + reason;
        return false;
    }
}
