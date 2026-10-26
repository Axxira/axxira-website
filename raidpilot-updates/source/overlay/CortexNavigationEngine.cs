using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// CORTEX navigation supervisor. It does not manufacture EFT coordinates:
/// native F8 anchors and the existing estimators remain the source of pose.
/// CORTEX arbitrates temporal sensor consistency, APPLIED local visual
/// corrections, observed F8 errors and the least disruptive next F8 cadence.
/// No frames, coordinates, paths, keyboard events or identifiers are persisted.
/// </summary>
internal sealed class CortexNavigationEngine
{
    private const double MaximumTrustedVisualAgeSeconds = 5.0;
    private const double MaximumLearnedVisualGraceSeconds = 9.0;
    private const double MaximumPoseAgeSeconds = 0.75;
    private const double MinimumIndependentVisualGapSeconds = 0.20;
    private const double MaximumIndependentVisualGapSeconds = 5.0;
    private const double MotionConflictRecoverySeconds = 6.0;
    private const double BadAnchorRecoverySeconds = 35.0;
    private const int ErrorSampleCapacity = 12;

    private string _mapId = string.Empty;
    private int _raidGeneration = -1;
    private nint _gameWindow;
    private long _lastPoseAt;
    private long _lastNativeAnchorAt;
    private long _lastTrustedVisualAt;
    private long _lastMotionConflictAt;
    private long _lastBadAnchorAt;
    private RealtimePose? _lastPose;
    private readonly Queue<double> _f8Errors = new(ErrorSampleCapacity);
    private readonly CortexLandmarkMemory _landmarks = new();
    private readonly CortexDriftRiskModel _driftRisk = new();
    private int _cleanAnchorsAfterBad;
    private long _landmarkConflicts;
    private long _visualAccepted;
    private long _visualRejected;
    private long _poseRejected;
    private long _f8Compared;
    private long _f8Bad;
    private long _nativeF8Intervals;
    private double _nativeF8IntervalSeconds;
    private long _nativeFastIntervals;
    private long _nativeMediumIntervals;
    private long _nativeSlowIntervals;

    internal long AcceptedVisualCorrections => _visualAccepted;
    internal long RejectedVisualCorrections => _visualRejected;
    internal long RejectedPoseObservations => _poseRejected;
    internal long ComparableF8Anchors => _f8Compared;
    internal long SuspiciousF8Anchors => _f8Bad;
    internal int RememberedLandmarks => _landmarks.Count;
    internal long LandmarkIdentityConflicts => _landmarkConflicts;
    internal CortexDriftRiskSnapshot DriftRisk => _driftRisk.Current;
    internal long MeasuredNativeF8Intervals => _nativeF8Intervals;
    internal double AverageNativeF8IntervalSeconds =>
        _nativeF8Intervals == 0 ? double.NaN
            : _nativeF8IntervalSeconds / _nativeF8Intervals;
    internal long ShortNativeF8Intervals => _nativeFastIntervals;
    internal long MediumNativeF8Intervals => _nativeMediumIntervals;
    internal long LongNativeF8Intervals => _nativeSlowIntervals;

    // Only a real EFT screenshot advances this clock. Visual keyframe
    // corrections may adjust the position, but cannot create a new F8.
    internal double SecondsSinceNativeF8(
        long timestamp, string mapId, int raidGeneration, nint gameWindow) =>
        !IsCurrentSession(mapId, raidGeneration, gameWindow)
        || _lastNativeAnchorAt <= 0 || timestamp < _lastNativeAnchorAt
            ? 0.0
            : (timestamp - _lastNativeAnchorAt) / (double)Stopwatch.Frequency;

    internal double? MedianF8ErrorMeters =>
        _f8Errors.Count == 0
            ? null
            : PrecisionDriftAudit.Median(_f8Errors.Order().ToArray());

    internal void Reset()
    {
        _mapId = string.Empty;
        _raidGeneration = -1;
        _gameWindow = nint.Zero;
        _lastPoseAt = 0;
        _lastNativeAnchorAt = 0;
        _lastTrustedVisualAt = 0;
        _lastMotionConflictAt = 0;
        _lastBadAnchorAt = 0;
        _lastPose = null;
        _f8Errors.Clear();
        _driftRisk.Reset();
        _landmarks.Reset();
        _landmarkConflicts = 0;
        _cleanAnchorsAfterBad = 0;
        _visualAccepted = _visualRejected = _poseRejected = 0;
        _f8Compared = _f8Bad = 0;
        _nativeF8Intervals = 0;
        _nativeF8IntervalSeconds = 0;
        _nativeFastIntervals = _nativeMediumIntervals = _nativeSlowIntervals = 0;
    }

    internal void ObservePose(
        long timestamp,
        string mapId,
        int raidGeneration,
        nint gameWindow,
        RealtimePose pose,
        bool movementCommanded,
        RealtimeMotionEvidence visualEvidence,
        bool correctionAppliedThisTick = false)
    {
        if (timestamp <= 0 || gameWindow == nint.Zero || string.IsNullOrWhiteSpace(mapId))
            return;

        EnsureSession(mapId, raidGeneration, gameWindow);
        if (_lastPoseAt != 0 && timestamp <= _lastPoseAt)
        {
            _poseRejected++;
            return;
        }

        if (!IsFinitePose(pose))
        {
            MarkMotionConflict(timestamp);
            _lastPose = null;
            _lastPoseAt = timestamp;
            _poseRejected++;
            return;
        }

        if (_lastPose is { } previous && _lastPoseAt > 0)
        {
            var deltaSeconds = (timestamp - _lastPoseAt) / (double)Stopwatch.Frequency;
            if (deltaSeconds > MaximumPoseAgeSeconds)
            {
                // A delayed UI tick cannot be treated as a continuous movement
                // measurement; do not invent the distance crossed during it.
                MarkMotionConflict(timestamp);
            }
            else if (deltaSeconds > 0 && !correctionAppliedThisTick)
            {
                // An accepted keyframe correction is not travel speed.
                // The estimator already bounded that correction separately.
                var distance = Math.Sqrt(Math.Pow(pose.X - previous.X, 2)
                    + Math.Pow(pose.Z - previous.Z, 2));
                var floorJump = Math.Abs(pose.Y - previous.Y);
                // Loose physical sanity bound; does not assume surveyed walls.
                if (distance > 2.0 + 15.0 * deltaSeconds
                    || floorJump > 3.0 + 12.0 * deltaSeconds)
                {
                    MarkMotionConflict(timestamp);
                }
            }
        }

        if (movementCommanded && visualEvidence == RealtimeMotionEvidence.Stationary)
        {
            // Could be a wall, a stuck capture, or a blocked player. Do not
            // silently strengthen visual GPS during a contradiction.
            MarkMotionConflict(timestamp);
        }

        _lastPose = pose;
        _lastPoseAt = timestamp;
    }

    /// <summary>
    /// Must be called only AFTER the existing relocalizer actually changed
    /// the pose. An uncommitted candidate must never refresh visual trust.
    /// </summary>
    internal bool NoteAppliedVisual(
        long timestamp,
        string mapId,
        int raidGeneration,
        nint gameWindow,
        VisualRelocalizationDecision correction,
        bool fromSharedAtlas,
        long? lowKeyframeId = null,
        long? superKeyframeId = null)
    {
        if (!IsCurrentSession(mapId, raidGeneration, gameWindow)
            || _lastNativeAnchorAt <= 0
            || timestamp <= _lastNativeAnchorAt
            || timestamp <= _lastTrustedVisualAt
            || !correction.ShouldCorrect
            || !MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                correction.Source,
                fromSharedAtlas,
                correction.Confidence,
                correction.PositionResidualMeters,
                correction.VerticalResidualMeters,
                correction.YawResidualDegrees))
        {
            _visualRejected++;
            return false;
        }

        var elapsed = _lastTrustedVisualAt <= 0
            ? double.PositiveInfinity
            : (timestamp - _lastTrustedVisualAt) / (double)Stopwatch.Frequency;
        if (elapsed < MinimumIndependentVisualGapSeconds
            || lowKeyframeId is not > 0
            || superKeyframeId is not > 0
            || correction.TargetPose is not { } targetPose)
        {
            _visualRejected++;
            return false;
        }

        // Visual confidence alone cannot prove independent geography. Require
        // the actual local low/high keyframe identities used by the matcher.
        var landmark = _landmarks.Observe(timestamp,
            lowKeyframeId.Value, superKeyframeId.Value, targetPose);
        if (landmark is CortexLandmarkResult.Invalid
            or CortexLandmarkResult.ConflictingPose)
        {
            _visualRejected++;
            if (landmark == CortexLandmarkResult.ConflictingPose)
            {
                _landmarkConflicts++;
                MarkMotionConflict(timestamp);
            }
            return false;
        }

        _lastTrustedVisualAt = timestamp;
        _visualAccepted++;
        return true;
    }

    /// <summary>
    /// Exactly one native anchor per screenshot. A comparable drift sample
    /// comes from the existing timestamp-validated PrecisionDriftAudit only.
    /// No visual correction can masquerade as a native F8 ground truth.
    /// </summary>
    internal void NoteNativeF8(
        long timestamp,
        string mapId,
        int raidGeneration,
        nint gameWindow,
        PrecisionDriftSample? comparable)
    {
        if (timestamp <= 0 || gameWindow == nint.Zero || string.IsNullOrWhiteSpace(mapId))
            return;
        EnsureSession(mapId, raidGeneration, gameWindow);
        if (timestamp <= _lastNativeAnchorAt) return;
        if (_lastNativeAnchorAt > 0)
        {
            var gap = (timestamp - _lastNativeAnchorAt) / (double)Stopwatch.Frequency;
            // A real measured cadence, independent from CORTEX assessments
            // and without storing screenshot paths, location or player inputs.
            if (gap is >= 2 and <= 60)
            {
                _nativeF8Intervals++;
                _nativeF8IntervalSeconds += gap;
                if (gap < 6) _nativeFastIntervals++;
                else if (gap < 10) _nativeMediumIntervals++;
                else _nativeSlowIntervals++;
            }
        }

        _lastNativeAnchorAt = timestamp;
        _lastPoseAt = 0;
        _lastPose = null;
        _lastTrustedVisualAt = 0;
        _landmarks.Reset();

        if (comparable is not { } sample
            || !double.IsFinite(sample.ErrorMeters)
            || !double.IsFinite(sample.HeadingErrorDegrees)
            || !double.IsFinite(sample.ModelUncertaintyMeters)
            || !double.IsFinite(sample.Seconds)
            || sample.ErrorMeters < 0
            || sample.HeadingErrorDegrees is < 0 or > 180
            || sample.ModelUncertaintyMeters < 0
            || sample.Seconds is < 4 or > 900)
            return;

        if (_f8Errors.Count == ErrorSampleCapacity) _f8Errors.Dequeue();
        _f8Errors.Enqueue(sample.ErrorMeters);
        _f8Compared++;
        // Only comparable NATIVE F8 residuals train the on-device risk model.
        // It never receives visual-only synthetic anchors or absolute positions.
        _driftRisk.Observe(sample);

        // Large actual residuals override optimistic estimator uncertainty.
        // Record the error, but do NOT change the estimator's F8 position.
        var unexpected = sample.ErrorMeters >
            Math.Max(2.5, 2.5 * sample.ModelUncertaintyMeters)
            || sample.HeadingErrorDegrees > 15.0;
        if (unexpected)
        {
            _f8Bad++;
            _lastBadAnchorAt = timestamp;
            _cleanAnchorsAfterBad = 0;
        }
        else
        {
            _cleanAnchorsAfterBad = Math.Min(3, _cleanAnchorsAfterBad + 1);
        }
    }

    internal CortexNavigationAssessment Assess(
        long timestamp,
        string mapId,
        int raidGeneration,
        nint gameWindow,
        RealtimePose pose,
        LocalizationHealthState health)
    {
        if (!IsCurrentSession(mapId, raidGeneration, gameWindow)
            || _lastNativeAnchorAt <= 0
            || timestamp <= 0
            || timestamp < _lastNativeAnchorAt
            || !IsFinitePose(pose))
            return new(CortexTrackingQuality.WaitingForF8, false, false,
                0.0, "Ancre F8 réelle requise");

        if (_lastPoseAt <= 0 || timestamp < _lastPoseAt
            || (timestamp - _lastPoseAt) / (double)Stopwatch.Frequency
                > MaximumPoseAgeSeconds)
            return new(CortexTrackingQuality.Degraded, false, false,
                0.0, "Suivi temporel périmé");

        var secondsSinceConflict = SecondsSince(_lastMotionConflictAt, timestamp);
        var secondsSinceBad = SecondsSince(_lastBadAnchorAt, timestamp);
        var badF8NotRecovered = _lastBadAnchorAt != 0
            && (secondsSinceBad < BadAnchorRecoverySeconds
                || _cleanAnchorsAfterBad < 2);
        var conflictsRecent = secondsSinceConflict < MotionConflictRecoverySeconds;
        var visualAge = SecondsSince(_lastTrustedVisualAt, timestamp);
        var visualFresh = visualAge <= MaximumTrustedVisualAgeSeconds;
        var reliableHealth = health is LocalizationHealthState.Green
            or LocalizationHealthState.Yellow;
        var safeGeometry = pose.PositionUncertaintyMeters < 2.25
            && pose.YawUncertaintyDegrees < 20.0;
        var recentF8 = SecondsSince(_lastNativeAnchorAt, timestamp) <= 45.0;
        // A brief extension from 5 to at most 9 seconds WITHOUT a new visual
        // hit is permissible only after native-F8 risk calibration has learned
        // low drift. The original visual proof must still be from this raid.
        // Never grant the 12 s tier from this learned grace period.
        var risk = _driftRisk.Current;
        var learnedGrace = !visualFresh
            && visualAge <= MaximumLearnedVisualGraceSeconds
            && risk.Calibrated && risk.MaxIntervalSeconds >= 8
            && _landmarks.Count > 0
            && health == LocalizationHealthState.Green
            && pose.PositionUncertaintyMeters < 1.25
            && pose.YawUncertaintyDegrees < 10.0;
        var eligible = (visualFresh || learnedGrace) && reliableHealth
            && safeGeometry && recentF8
            && !badF8NotRecovered && !conflictsRecent;
        var strong = eligible && visualFresh &&
            _landmarks.CountRecentDistinct(timestamp, MaximumIndependentVisualGapSeconds) >= 2;

        // This number is a transparent *quality indicator*, not a statistical
        // guarantee of localization accuracy. Actual errors come from F8.
        var uncertaintyScore = Math.Exp(-pose.PositionUncertaintyMeters / 5.0);
        var score = Math.Clamp(uncertaintyScore * (eligible ? 1.0 : 0.65)
            * (conflictsRecent || badF8NotRecovered ? 0.35 : 1.0), 0.0, 1.0);

        if (badF8NotRecovered)
            return new(CortexTrackingQuality.Degraded, false, false,
                score, "Dérive observée au F8 — repli prudent");
        if (conflictsRecent)
            return new(CortexTrackingQuality.Degraded, false, false,
                score, "Mouvement et capteurs contradictoires");
        if (!eligible)
        {
            // Report the first actionable gating reason, rather than hiding
            // every failure behind the generic "predicted" label.
            var reason = !visualFresh
                ? "Aucune correction visuelle locale récente validée"
                : !reliableHealth
                    ? "Santé GPS orange/rouge — intervalle F8 réduit"
                    : !safeGeometry
                        ? $"Incertitude élevée : {pose.PositionUncertaintyMeters:F2} m / {pose.YawUncertaintyDegrees:F1}°"
                        : !recentF8
                            ? "Ancre F8 réelle trop ancienne"
                            : "Confiance de localisation insuffisante";
            return new(CortexTrackingQuality.Predicted, false, false,
                score, reason);
        }
        // The learned empirical error ceiling vetoes unsafe extensions.
        // The short grace requires an already-accepted local visual anchor, a
        // sufficiently healthy current pose, and six genuine F8 comparisons.
        // It never promotes an unverified visual correction or replaces a F8.
        if (risk.Calibrated && risk.MaxIntervalSeconds <= 4)
            return new(CortexTrackingQuality.Predicted, false, false,
                score, risk.Reason);
        if (risk.Calibrated && risk.MaxIntervalSeconds <= 8)
            strong = false;
        return new(CortexTrackingQuality.VisuallySupported, true, strong,
            score, learnedGrace
                ? "Grâce adaptative de 8 s : repère local et dérive F8 maîtrisée"
                : risk.Calibrated && risk.MaxIntervalSeconds <= 8
                    ? risk.Reason
                    : strong ? "Deux corrections visuelles locales concordantes"
                        : "Correction visuelle locale validée");
    }

    internal string Diagnostic(CortexNavigationAssessment assessment)
    {
        var median = MedianF8ErrorMeters is { } error
            ? error.ToString("F2") + " m"
            : "non mesurée";
        var risk = _driftRisk.Current;
        var cadenceText = _nativeF8Intervals > 0
            ? $"F8 réels : {_nativeF8Intervals} intervalles, moyenne "
                + $"{_nativeF8IntervalSeconds / _nativeF8Intervals:F1} s"
                + $" (moins de 6 s : {_nativeFastIntervals}; 6 à 10 s : {_nativeMediumIntervals};"
                + $" 10 s et plus : {_nativeSlowIntervals})"
            : "F8 réels : cadence pas encore mesurée";
        var riskText = risk.Calibrated
            ? $"F8 apprentissage {risk.SampleCount} : écart empirique P90 à 8 s {risk.EstimatedErrorAt8Meters:F2} m"
                + $", à 12 s {risk.EstimatedErrorAt12Meters:F2} m; plafond {risk.MaxIntervalSeconds} s"
            : $"F8 apprentissage {risk.SampleCount}/{CortexDriftRiskModel.MinimumSamples} échantillons"
                + " (pas encore calibré)";
        return "CORTEX : " + assessment.Quality
            + $" · indice {assessment.QualityIndicator:F2} (non métrique)"
            + " · " + assessment.Reason
            + $"\nF8 comparables {_f8Compared}, dérives fortes {_f8Bad}, médiane {median}"
            + "\n" + riskText
            + "\n" + cadenceText
            + $"\nVision locale acceptée {_visualAccepted}, refusée {_visualRejected}, "
            + $"poses refusées {_poseRejected}"
            + $"\nMémoire CORTEX : {_landmarks.Count} repères, "
            + $"{_landmarks.Repeated} revisites, {_landmarkConflicts} conflits d'identité"
            + "\nCadence F8 cible : " + (assessment.AllowStrongExtension
                ? "12 s" : assessment.AllowExtendedF8 ? "8 s" : "4 s")
            + "\nExtension F8 : " + (assessment.AllowExtendedF8
                ? assessment.AllowStrongExtension ? "forte" : "modérée"
                : "désactivée");
    }

    private void EnsureSession(string mapId, int raidGeneration, nint gameWindow)
    {
        if (IsCurrentSession(mapId, raidGeneration, gameWindow)) return;
        Reset();
        _mapId = mapId;
        _raidGeneration = raidGeneration;
        _gameWindow = gameWindow;
    }

    private bool IsCurrentSession(string mapId, int raidGeneration, nint gameWindow) =>
        gameWindow != nint.Zero
        && _gameWindow == gameWindow
        && _raidGeneration == raidGeneration
        && !string.IsNullOrWhiteSpace(mapId)
        && string.Equals(_mapId, mapId, StringComparison.OrdinalIgnoreCase);

    private void MarkMotionConflict(long timestamp)
    {
        _lastMotionConflictAt = Math.Max(_lastMotionConflictAt, timestamp);
        _landmarks.Reset();
        _lastTrustedVisualAt = 0;
    }

    private static double SecondsSince(long then, long now) =>
        then == 0 || now < then
            ? double.PositiveInfinity
            : (now - then) / (double)Stopwatch.Frequency;

    private static bool IsFinitePose(RealtimePose pose) =>
        double.IsFinite(pose.X) && double.IsFinite(pose.Y)
        && double.IsFinite(pose.Z) && double.IsFinite(pose.YawDegrees)
        && double.IsFinite(pose.PositionUncertaintyMeters)
        && double.IsFinite(pose.YawUncertaintyDegrees)
        && pose.PositionUncertaintyMeters >= 0
        && pose.YawUncertaintyDegrees >= 0;
}

internal enum CortexTrackingQuality
{
    WaitingForF8,
    Predicted,
    VisuallySupported,
    Degraded,
}

internal readonly record struct CortexNavigationAssessment(
    CortexTrackingQuality Quality,
    bool AllowExtendedF8,
    bool AllowStrongExtension,
    double QualityIndicator,
    string Reason);
