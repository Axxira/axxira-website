using System.Diagnostics;

namespace RaidPilot.Overlay;

internal enum LocalizationHealthState
{
    Green,
    Yellow,
    Orange,
    Red,
}

internal enum VisualCorrectionMode
{
    None,
    Soft,
    Medium,
    HardSnap,
}

internal enum VisualRelocalizationSource
{
    None,
    LowResolution,
    SuperKeyframe,
    Consensus,
}

internal sealed record VisualRelocalizationSettings
{
    internal double SoftCorrectionMaximumMeters { get; init; } = 0.50;
    internal double MediumCorrectionMaximumMeters { get; init; } = 3.0;
    internal double SoftMinimumConfidence { get; init; } = 0.68;
    internal double MediumMinimumConfidence { get; init; } = 0.78;
    internal double HardSnapMinimumConfidence { get; init; } = 0.92;
    internal double MaximumLocalVerticalResidualMeters { get; init; } = 3.0;
    internal double MaximumLocalYawResidualDegrees { get; init; } = 40.0;
    internal double MaximumLoopClosureYawResidualDegrees { get; init; } = 115.0;
    internal double CrossRecognizerPositionToleranceMeters { get; init; } = 4.0;
    internal double CrossRecognizerVerticalToleranceMeters { get; init; } = 3.0;
    internal double CrossRecognizerYawToleranceDegrees { get; init; } = 20.0;
    internal double StrongRegionalConsensus { get; init; } = 0.72;
    internal double StrongUniquenessMargin { get; init; } = 0.04;

    internal double GreenUncertaintyMeters { get; init; } = 1.5;
    internal double YellowUncertaintyMeters { get; init; } = 4.0;
    internal double OrangeUncertaintyMeters { get; init; } = 8.0;
    internal double GreenMaximumConfirmationAgeSeconds { get; init; } = 8.0;
    internal double YellowMaximumConfirmationAgeSeconds { get; init; } = 20.0;
    internal double OrangeMaximumConfirmationAgeSeconds { get; init; } = 45.0;

    internal double MovingLowResolutionGreenIntervalSeconds { get; init; } = 0.45;
    internal double MovingLowResolutionYellowIntervalSeconds { get; init; } = 0.30;
    internal double MovingLowResolutionOrangeIntervalSeconds { get; init; } = 0.20;
    internal double MovingLowResolutionRedIntervalSeconds { get; init; } = 0.12;
    internal double StationaryLowResolutionIntervalSeconds { get; init; } = 0.10;

    internal double HighResolutionGreenIntervalSeconds { get; init; } = 0.60;
    internal double HighResolutionYellowIntervalSeconds { get; init; } = 0.45;
    internal double HighResolutionOrangeIntervalSeconds { get; init; } = 0.28;
    internal double HighResolutionRedIntervalSeconds { get; init; } = 0.18;
    internal double HighResolutionMinimumUncertaintyMeters { get; init; } = 1.25;
}

internal readonly record struct VisualRelocalizationDecision(
    VisualCorrectionMode Mode,
    VisualRelocalizationSource Source,
    VisualKeyframePose? TargetPose,
    double Confidence,
    double PositionResidualMeters,
    double VerticalResidualMeters,
    double YawResidualDegrees,
    double PositionGain,
    double YawGain,
    double MaximumTranslationStepMeters,
    double MaximumYawStepDegrees,
    bool GlobalLoopClosure,
    LocalizationHealthState Health,
    string Reason)
{
    internal bool ShouldCorrect =>
        Mode != VisualCorrectionMode.None && TargetPose.HasValue;

    internal static VisualRelocalizationDecision None(
        LocalizationHealthState health,
        string reason) => new(
            VisualCorrectionMode.None,
            VisualRelocalizationSource.None,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            health,
            reason);
}

internal readonly record struct VisualRelocalizationMetrics(
    long Evaluations,
    long AcceptedCorrections,
    long RejectedCandidates,
    long SoftCorrections,
    long MediumCorrections,
    long HardSnaps,
    long LoopClosures,
    double MeanAcceptedResidualMeters,
    double MaximumAcceptedResidualMeters,
    double SecondsSinceAbsoluteConfirmation,
    LocalizationHealthState Health);

/// <summary>
/// Arbitre de relocalisation au-dessus des deux moteurs de keyframes.
/// Il ne remplace jamais l'odométrie : il décide quand une preuve visuelle est
/// assez forte pour corriger l'état temps réel, avec une cadence et une force
/// de correction adaptées à la santé courante de la localisation.
/// </summary>
internal sealed class VisualRelocalizationEngineV2
{
    private readonly VisualRelocalizationSettings _settings;
    private long _sessionStartedAt;
    private long _lastAbsoluteConfirmationAt;
    private long _lastLowResolutionAttemptAt;
    private long _lastHighResolutionAttemptAt;
    private long _evaluations;
    private long _accepted;
    private long _rejected;
    private long _soft;
    private long _medium;
    private long _hard;
    private long _loopClosures;
    private double _acceptedResidualTotal;
    private double _maximumAcceptedResidual;
    private LocalizationHealthState _health = LocalizationHealthState.Yellow;

    internal VisualRelocalizationEngineV2(
        VisualRelocalizationSettings? settings = null)
    {
        _settings = settings ?? new VisualRelocalizationSettings();
        ValidateSettings(_settings);
    }

    internal LocalizationHealthState Health => _health;

    internal void BeginSession(long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        _sessionStartedAt = timestamp;
        _lastAbsoluteConfirmationAt = 0;
        _lastLowResolutionAttemptAt = 0;
        _lastHighResolutionAttemptAt = 0;
        _health = LocalizationHealthState.Yellow;
    }

    internal void NoteAbsoluteAnchor(long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        if (_sessionStartedAt == 0)
        {
            _sessionStartedAt = timestamp;
        }
        _lastAbsoluteConfirmationAt = Math.Max(
            _lastAbsoluteConfirmationAt,
            timestamp);
        _health = LocalizationHealthState.Green;
    }

    internal LocalizationHealthState ObserveTracking(
        RealtimePose pose,
        long timestamp)
    {
        ValidatePose(pose);
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }
        if (_sessionStartedAt == 0)
        {
            _sessionStartedAt = timestamp;
        }

        _health = CalculateHealth(
            pose.PositionUncertaintyMeters,
            SecondsSinceAbsoluteConfirmation(timestamp));
        return _health;
    }

    internal bool ShouldRunLowResolution(
        bool movementInputActive,
        long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        var interval = movementInputActive
            ? _health switch
            {
                LocalizationHealthState.Green =>
                    _settings.MovingLowResolutionGreenIntervalSeconds,
                LocalizationHealthState.Yellow =>
                    _settings.MovingLowResolutionYellowIntervalSeconds,
                LocalizationHealthState.Orange =>
                    _settings.MovingLowResolutionOrangeIntervalSeconds,
                _ => _settings.MovingLowResolutionRedIntervalSeconds,
            }
            : _settings.StationaryLowResolutionIntervalSeconds;

        if (!IntervalElapsed(_lastLowResolutionAttemptAt, timestamp, interval))
        {
            return false;
        }

        _lastLowResolutionAttemptAt = timestamp;
        return true;
    }

    internal bool ShouldRunHighResolution(
        RealtimePose pose,
        bool superKeyframesAvailable,
        long timestamp)
    {
        ValidatePose(pose);
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }
        if (!superKeyframesAvailable)
        {
            return false;
        }

        ObserveTracking(pose, timestamp);
        var useful = pose.PositionUncertaintyMeters
            >= _settings.HighResolutionMinimumUncertaintyMeters
            || _health is LocalizationHealthState.Orange
                or LocalizationHealthState.Red;
        if (!useful)
        {
            return false;
        }

        var interval = _health switch
        {
            LocalizationHealthState.Green =>
                _settings.HighResolutionGreenIntervalSeconds,
            LocalizationHealthState.Yellow =>
                _settings.HighResolutionYellowIntervalSeconds,
            LocalizationHealthState.Orange =>
                _settings.HighResolutionOrangeIntervalSeconds,
            _ => _settings.HighResolutionRedIntervalSeconds,
        };

        if (!IntervalElapsed(_lastHighResolutionAttemptAt, timestamp, interval))
        {
            return false;
        }

        _lastHighResolutionAttemptAt = timestamp;
        return true;
    }

    internal VisualRelocalizationDecision Evaluate(
        RealtimePose estimatedPoseAtCapture,
        VisualKeyframeRecognition lowResolution,
        SuperKeyframeRecognition superKeyframe,
        bool superKeyframesAvailable,
        long timestamp)
    {
        ValidatePose(estimatedPoseAtCapture);
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        _evaluations++;
        ObserveTracking(estimatedPoseAtCapture, timestamp);

        var lowConfirmed = lowResolution.IsConfirmed
            && lowResolution.ExactPose is { };
        var superConfirmed = superKeyframe.IsConfirmed
            && superKeyframe.ExactPose is { };

        if (superKeyframesAvailable && !superConfirmed)
        {
            if (lowConfirmed)
            {
                _rejected++;
            }
            return VisualRelocalizationDecision.None(
                _health,
                "SuperKeyframe requis mais non confirmé");
        }

        if (!superKeyframesAvailable && !lowConfirmed)
        {
            return VisualRelocalizationDecision.None(
                _health,
                "aucun repère confirmé");
        }

        VisualKeyframePose target;
        VisualRelocalizationSource source;
        var confidence = 0.0;
        var globalLoopClosure = false;

        if (superConfirmed)
        {
            target = superKeyframe.ExactPose!.Value;
            source = VisualRelocalizationSource.SuperKeyframe;
            confidence = ScoreSuper(superKeyframe);
            globalLoopClosure = superKeyframe.GlobalLoopClosure;

            if (lowConfirmed)
            {
                var lowPose = lowResolution.ExactPose!.Value;
                if (!AnchorsConsistent(lowPose, target))
                {
                    _rejected++;
                    return VisualRelocalizationDecision.None(
                        _health,
                        "désaccord entre keyframe basse et haute résolution");
                }

                source = VisualRelocalizationSource.Consensus;
                confidence = Math.Min(
                    1.0,
                    Math.Max(confidence, ScoreLow(lowResolution)) + 0.08);
            }
        }
        else
        {
            target = lowResolution.ExactPose!.Value;
            source = VisualRelocalizationSource.LowResolution;
            confidence = ScoreLow(lowResolution);
        }

        var positionResidual = HorizontalDistance(
            estimatedPoseAtCapture,
            target);
        var verticalResidual = Math.Abs(
            estimatedPoseAtCapture.Y - target.Y);
        var yawResidual = AngleDistance(
            estimatedPoseAtCapture.YawDegrees,
            target.YawDegrees);

        var maximumYawResidual = globalLoopClosure
            ? _settings.MaximumLoopClosureYawResidualDegrees
            : _settings.MaximumLocalYawResidualDegrees;
        if ((!globalLoopClosure
                && verticalResidual
                    > _settings.MaximumLocalVerticalResidualMeters)
            || yawResidual > maximumYawResidual)
        {
            _rejected++;
            return VisualRelocalizationDecision.None(
                _health,
                "géométrie incompatible avec la pose candidate");
        }

        var strongSuper = superConfirmed
            && superKeyframe.RegionalConsensus
                >= _settings.StrongRegionalConsensus
            && superKeyframe.UniquenessMargin
                >= _settings.StrongUniquenessMargin;
        var hardProof = source == VisualRelocalizationSource.Consensus
            || globalLoopClosure
            || strongSuper;

        VisualCorrectionMode mode;
        double positionGain;
        double yawGain;
        double maxStep;
        double maxYawStep;

        if (positionResidual <= _settings.SoftCorrectionMaximumMeters
            && confidence >= _settings.SoftMinimumConfidence)
        {
            mode = VisualCorrectionMode.Soft;
            positionGain = 0.35;
            yawGain = 0.35;
            maxStep = 0.30;
            maxYawStep = 2.0;
        }
        else if (positionResidual <= _settings.MediumCorrectionMaximumMeters
            && confidence >= _settings.MediumMinimumConfidence)
        {
            mode = VisualCorrectionMode.Medium;
            positionGain = 0.70;
            yawGain = 0.65;
            maxStep = 1.75;
            maxYawStep = 8.0;
        }
        else if (confidence >= _settings.HardSnapMinimumConfidence
            && hardProof)
        {
            mode = VisualCorrectionMode.HardSnap;
            positionGain = 1.0;
            yawGain = 1.0;
            maxStep = double.PositiveInfinity;
            maxYawStep = 180.0;
        }
        else
        {
            _rejected++;
            return VisualRelocalizationDecision.None(
                _health,
                "preuve insuffisante pour la taille du résidu");
        }

        _accepted++;
        _acceptedResidualTotal += positionResidual;
        _maximumAcceptedResidual = Math.Max(
            _maximumAcceptedResidual,
            positionResidual);
        switch (mode)
        {
            case VisualCorrectionMode.Soft:
                _soft++;
                break;
            case VisualCorrectionMode.Medium:
                _medium++;
                break;
            case VisualCorrectionMode.HardSnap:
                _hard++;
                if (globalLoopClosure)
                {
                    _loopClosures++;
                }
                break;
        }

        _lastAbsoluteConfirmationAt = Math.Max(
            _lastAbsoluteConfirmationAt,
            timestamp);
        _health = LocalizationHealthState.Green;

        return new VisualRelocalizationDecision(
            mode,
            source,
            target,
            confidence,
            positionResidual,
            verticalResidual,
            yawResidual,
            positionGain,
            yawGain,
            maxStep,
            maxYawStep,
            globalLoopClosure,
            _health,
            mode switch
            {
                VisualCorrectionMode.Soft => "micro-correction visuelle",
                VisualCorrectionMode.Medium => "rattrapage visuel amorti",
                _ when globalLoopClosure => "loop closure confirmé",
                _ => "relocalisation absolue forte",
            });
    }

    internal VisualRelocalizationMetrics GetMetrics(long timestamp)
    {
        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        return new VisualRelocalizationMetrics(
            _evaluations,
            _accepted,
            _rejected,
            _soft,
            _medium,
            _hard,
            _loopClosures,
            _accepted == 0 ? 0 : _acceptedResidualTotal / _accepted,
            _maximumAcceptedResidual,
            SecondsSinceAbsoluteConfirmation(timestamp),
            _health);
    }

    internal bool AnchorsConsistent(
        VisualKeyframePose lowResolution,
        VisualKeyframePose highResolution)
    {
        var horizontal = Math.Sqrt(
            Math.Pow(lowResolution.X - highResolution.X, 2)
            + Math.Pow(lowResolution.Z - highResolution.Z, 2));
        var vertical = Math.Abs(
            lowResolution.Y - highResolution.Y);
        var yaw = AngleDistance(
            lowResolution.YawDegrees,
            highResolution.YawDegrees);

        return horizontal <= _settings.CrossRecognizerPositionToleranceMeters
            && vertical <= _settings.CrossRecognizerVerticalToleranceMeters
            && yaw <= _settings.CrossRecognizerYawToleranceDegrees;
    }

    private LocalizationHealthState CalculateHealth(
        double uncertaintyMeters,
        double confirmationAgeSeconds)
    {
        if (uncertaintyMeters <= _settings.GreenUncertaintyMeters
            && confirmationAgeSeconds
                <= _settings.GreenMaximumConfirmationAgeSeconds)
        {
            return LocalizationHealthState.Green;
        }
        if (uncertaintyMeters <= _settings.YellowUncertaintyMeters
            && confirmationAgeSeconds
                <= _settings.YellowMaximumConfirmationAgeSeconds)
        {
            return LocalizationHealthState.Yellow;
        }
        if (uncertaintyMeters <= _settings.OrangeUncertaintyMeters
            && confirmationAgeSeconds
                <= _settings.OrangeMaximumConfirmationAgeSeconds)
        {
            return LocalizationHealthState.Orange;
        }
        return LocalizationHealthState.Red;
    }

    private double SecondsSinceAbsoluteConfirmation(long timestamp)
    {
        var origin = _lastAbsoluteConfirmationAt > 0
            ? _lastAbsoluteConfirmationAt
            : _sessionStartedAt;
        if (origin <= 0 || timestamp <= origin)
        {
            return 0;
        }
        return (timestamp - origin) / (double)Stopwatch.Frequency;
    }

    private static double ScoreLow(
        VisualKeyframeRecognition recognition)
    {
        var descriptorQuality = 1.0 - Math.Clamp(
            recognition.DescriptorDistance / 0.16,
            0,
            1);
        var uniquenessQuality = Math.Clamp(
            recognition.UniquenessMargin / 0.10,
            0,
            1);
        var temporalQuality = Math.Clamp(
            recognition.ConsecutiveMatches / 4.0,
            0,
            1);
        return Math.Clamp(
            0.62
            + (descriptorQuality * 0.14)
            + (uniquenessQuality * 0.10)
            + (temporalQuality * 0.08),
            0,
            0.94);
    }

    private static double ScoreSuper(
        SuperKeyframeRecognition recognition)
    {
        var descriptorQuality = 1.0 - Math.Clamp(
            recognition.CompositeDistance / 0.30,
            0,
            1);
        var uniquenessQuality = Math.Clamp(
            recognition.UniquenessMargin / 0.08,
            0,
            1);
        var temporalQuality = Math.Clamp(
            recognition.ConsecutiveMatches / 4.0,
            0,
            1);
        return Math.Clamp(
            0.60
            + (descriptorQuality * 0.12)
            + (uniquenessQuality * 0.08)
            + (Math.Clamp(recognition.RegionalConsensus, 0, 1) * 0.12)
            + (temporalQuality * 0.08),
            0,
            0.98);
    }

    private static double HorizontalDistance(
        RealtimePose estimate,
        VisualKeyframePose target) =>
        Math.Sqrt(
            Math.Pow(estimate.X - target.X, 2)
            + Math.Pow(estimate.Z - target.Z, 2));

    private static double AngleDistance(double a, double b)
    {
        var difference = (a - b) % 360.0;
        if (difference <= -180.0)
        {
            difference += 360.0;
        }
        else if (difference > 180.0)
        {
            difference -= 360.0;
        }
        return Math.Abs(difference);
    }

    private static bool IntervalElapsed(
        long lastTimestamp,
        long timestamp,
        double seconds)
    {
        if (lastTimestamp <= 0)
        {
            return true;
        }
        return timestamp - lastTimestamp
            >= SecondsToTimestampTicks(seconds);
    }

    private static long SecondsToTimestampTicks(double seconds) =>
        (long)Math.Ceiling(
            Math.Max(0, seconds) * Stopwatch.Frequency);

    private static void ValidatePose(RealtimePose pose)
    {
        if (!new[]
            {
                pose.X,
                pose.Y,
                pose.Z,
                pose.YawDegrees,
                pose.PositionUncertaintyMeters,
                pose.YawUncertaintyDegrees,
            }.All(double.IsFinite)
            || pose.PositionUncertaintyMeters < 0
            || pose.YawUncertaintyDegrees < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pose));
        }
    }

    private static void ValidateSettings(
        VisualRelocalizationSettings settings)
    {
        var values = new[]
        {
            settings.SoftCorrectionMaximumMeters,
            settings.MediumCorrectionMaximumMeters,
            settings.SoftMinimumConfidence,
            settings.MediumMinimumConfidence,
            settings.HardSnapMinimumConfidence,
            settings.MaximumLocalVerticalResidualMeters,
            settings.MaximumLocalYawResidualDegrees,
            settings.MaximumLoopClosureYawResidualDegrees,
            settings.CrossRecognizerPositionToleranceMeters,
            settings.CrossRecognizerVerticalToleranceMeters,
            settings.CrossRecognizerYawToleranceDegrees,
            settings.StrongRegionalConsensus,
            settings.StrongUniquenessMargin,
            settings.GreenUncertaintyMeters,
            settings.YellowUncertaintyMeters,
            settings.OrangeUncertaintyMeters,
            settings.GreenMaximumConfirmationAgeSeconds,
            settings.YellowMaximumConfirmationAgeSeconds,
            settings.OrangeMaximumConfirmationAgeSeconds,
            settings.MovingLowResolutionGreenIntervalSeconds,
            settings.MovingLowResolutionYellowIntervalSeconds,
            settings.MovingLowResolutionOrangeIntervalSeconds,
            settings.MovingLowResolutionRedIntervalSeconds,
            settings.StationaryLowResolutionIntervalSeconds,
            settings.HighResolutionGreenIntervalSeconds,
            settings.HighResolutionYellowIntervalSeconds,
            settings.HighResolutionOrangeIntervalSeconds,
            settings.HighResolutionRedIntervalSeconds,
            settings.HighResolutionMinimumUncertaintyMeters,
        };

        if (values.Any(value => !double.IsFinite(value) || value < 0)
            || settings.MediumCorrectionMaximumMeters
                < settings.SoftCorrectionMaximumMeters
            || settings.SoftMinimumConfidence > 1
            || settings.MediumMinimumConfidence > 1
            || settings.HardSnapMinimumConfidence > 1
            || settings.StrongRegionalConsensus > 1
            || settings.GreenUncertaintyMeters
                > settings.YellowUncertaintyMeters
            || settings.YellowUncertaintyMeters
                > settings.OrangeUncertaintyMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
    }
}
