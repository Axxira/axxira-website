namespace RaidPilot.Overlay;

/// <summary>
/// Paramètres calibrables utilisés par <see cref="RealtimePoseEstimator"/>.
/// Les sensibilités expriment des degrés de yaw par compteur horizontal reçu.
/// </summary>
public sealed record RealtimePoseEstimatorSettings
{
    public double HipYawDegreesPerMouseCount { get; init; } = 0.05;

    public double AdsYawDegreesPerMouseCount { get; init; } = 0.025;

    public double WalkSpeedMetersPerSecond { get; init; } = 2.2;

    public double SprintSpeedMetersPerSecond { get; init; } = 5.7;

    public double SlowSpeedMetersPerSecond { get; init; } = 1.0;

    /// <summary>
    /// EFT ralentit le déplacement arrière et les pas latéraux. Les modéliser
    /// séparément évite que les petits repositionnements fassent dériver le
    /// point aussi vite qu'une marche avant pleine vitesse.
    /// </summary>
    public double BackwardSpeedMultiplier { get; init; } = 0.72;

    public double StrafeSpeedMultiplier { get; init; } = 0.82;

    /// <summary>
    /// Accélération horizontale maximale. Une valeur finie évite de compter la
    /// pleine vitesse dès la première trame après une pression de touche.
    /// </summary>
    public double AccelerationMetersPerSecondSquared { get; init; } = 8.0;

    /// <summary>
    /// Freinage horizontal maximal quand les touches sont relâchées ou que la
    /// direction commandée change brusquement.
    /// </summary>
    public double DecelerationMetersPerSecondSquared { get; init; } = 12.0;

    public double InitialPositionUncertaintyMeters { get; init; } = 0.5;

    public double InitialYawUncertaintyDegrees { get; init; } = 0.5;

    public double PositionDriftMetersPerSecond { get; init; } = 0.02;

    public double DistanceErrorFraction { get; init; } = 0.05;

    /// <summary>
    /// Marge ajoutée tant qu'un déplacement est commandé mais ne peut pas être
    /// confirmé. Elle fait remonter rapidement l'alerte F8 en cas d'obstacle,
    /// de terrain lent ou de stamina épuisée, sans rendre le suivi inutilisable.
    /// </summary>
    public double UnobservedMovementUncertaintyMetersPerSecond { get; init; } = 0.8;

    public double YawDriftDegreesPerSecond { get; init; } = 0.05;

    public double MouseYawErrorFraction { get; init; } = 0.03;

    public double ConfirmedMovementUncertaintyMetersPerSecond { get; init; } = 0.18;

    public double ConfirmedStationaryUncertaintyMetersPerSecond { get; init; } = 0.06;
}

/// <summary>
/// Indice passif fourni par l'image du jeu. Il ne tente jamais de convertir
/// des pixels en mètres; il sert uniquement à confirmer un mouvement ou un
/// arrêt suffisamment évident.
/// </summary>
public enum RealtimeMotionEvidence
{
    Unknown,
    Moving,
    Stationary,
}

/// <summary>
/// Pose extrapolée depuis la dernière capture de position connue.
/// </summary>
public readonly record struct RealtimePose(
    double X,
    double Y,
    double Z,
    double YawDegrees,
    double PositionUncertaintyMeters,
    double YawUncertaintyDegrees);

/// <summary>
/// Estimateur inertiel simple et déterministe. Une capture fournit l'ancre
/// absolue; les entrées de déplacement et de souris extrapolent ensuite la pose.
/// </summary>
public sealed class RealtimePoseEstimator
{
    private const double DegreesToRadians = Math.PI / 180.0;

    private RealtimePoseEstimatorSettings _settings;
    private double _velocityX;
    private double _velocityZ;

    public RealtimePoseEstimator(RealtimePoseEstimatorSettings? settings = null)
    {
        _settings = settings ?? new RealtimePoseEstimatorSettings();
        ValidateSettings(_settings);
    }

    public RealtimePoseEstimatorSettings Settings => _settings;

    public RealtimePose? CurrentPose { get; private set; }

    public bool IsAnchored => CurrentPose.HasValue;

    /// <summary>
    /// Applique une calibration plus récente sans perdre la pose, la vitesse
    /// ni l'incertitude déjà accumulées pendant le raid.
    /// </summary>
    public void ApplySettings(RealtimePoseEstimatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);
        _settings = settings;
    }

    /// <summary>
    /// Applique une petite correction relative de cap issue d'une seconde
    /// source (vision). La position n'est jamais déplacée. La vélocité est
    /// tournée du même angle pour garder l'état dynamique cohérent.
    /// </summary>
    public RealtimePose ApplyYawCorrection(double correctionDegrees)
    {
        if (CurrentPose is not { } pose)
        {
            throw new InvalidOperationException("Une pose courante est requise pour corriger le cap.");
        }
        if (!double.IsFinite(correctionDegrees)
            || Math.Abs(correctionDegrees) > 5.0)
        {
            throw new ArgumentOutOfRangeException(nameof(correctionDegrees));
        }

        var radians = correctionDegrees * DegreesToRadians;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var correctedVelocityX = (_velocityX * cosine) + (_velocityZ * sine);
        var correctedVelocityZ = (-_velocityX * sine) + (_velocityZ * cosine);

        var corrected = pose with
        {
            YawDegrees = Normalize360(pose.YawDegrees + correctionDegrees),
        };
        CurrentPose = corrected;
        _velocityX = correctedVelocityX;
        _velocityZ = correctedVelocityZ;
        return corrected;
    }

    /// <summary>
    /// Remplace l'estimation par une mesure absolue et remet l'incertitude à sa
    /// valeur initiale.
    /// </summary>
    public RealtimePose Anchor(ScreenshotObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!AreFinite(observation.X, observation.Y, observation.Z, observation.YawDegrees))
        {
            throw new ArgumentException("La capture contient une pose non finie.", nameof(observation));
        }

        var pose = new RealtimePose(
            observation.X,
            observation.Y,
            observation.Z,
            Normalize360(observation.YawDegrees),
            _settings.InitialPositionUncertaintyMeters,
            _settings.InitialYawUncertaintyDegrees);

        CurrentPose = pose;
        _velocityX = 0.0;
        _velocityZ = 0.0;
        return pose;
    }

    /// <summary>
    /// Recale l'état présent avec une capture prise dans le passé. Le mouvement
    /// estimé depuis l'instant de capture est conservé et tourné selon l'erreur
    /// de cap mesurée, ce qui évite que le marqueur revienne en arrière pendant
    /// que le fichier de capture est détecté et analysé.
    /// </summary>
    public RealtimePose ReanchorFromHistoricalPose(
        ScreenshotObservation observation,
        RealtimePose estimatedPoseAtCapture)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (CurrentPose is not { } currentPose)
        {
            throw new InvalidOperationException("Une pose courante est requise pour effectuer un recalage historique.");
        }

        if (!AreFinite(observation.X, observation.Y, observation.Z, observation.YawDegrees)
            || !AreFinite(
                estimatedPoseAtCapture.X,
                estimatedPoseAtCapture.Y,
                estimatedPoseAtCapture.Z,
                estimatedPoseAtCapture.YawDegrees,
                estimatedPoseAtCapture.PositionUncertaintyMeters,
                estimatedPoseAtCapture.YawUncertaintyDegrees)
            || estimatedPoseAtCapture.PositionUncertaintyMeters < 0.0
            || estimatedPoseAtCapture.YawUncertaintyDegrees < 0.0)
        {
            throw new ArgumentException("La capture et la pose historique doivent contenir des valeurs finies et valides.");
        }

        var yawCorrection = NormalizeSigned180(
            observation.YawDegrees - estimatedPoseAtCapture.YawDegrees);
        var correctionRadians = yawCorrection * DegreesToRadians;
        var correctionCosine = Math.Cos(correctionRadians);
        var correctionSine = Math.Sin(correctionRadians);

        var relativeX = currentPose.X - estimatedPoseAtCapture.X;
        var relativeZ = currentPose.Z - estimatedPoseAtCapture.Z;
        var correctedRelativeX = (relativeX * correctionCosine) + (relativeZ * correctionSine);
        var correctedRelativeZ = (-relativeX * correctionSine) + (relativeZ * correctionCosine);

        var correctedVelocityX = (_velocityX * correctionCosine) + (_velocityZ * correctionSine);
        var correctedVelocityZ = (-_velocityX * correctionSine) + (_velocityZ * correctionCosine);

        var correctedPose = new RealtimePose(
            observation.X + correctedRelativeX,
            observation.Y,
            observation.Z + correctedRelativeZ,
            Normalize360(currentPose.YawDegrees + yawCorrection),
            _settings.InitialPositionUncertaintyMeters
                + Math.Max(
                    0.0,
                    currentPose.PositionUncertaintyMeters
                        - estimatedPoseAtCapture.PositionUncertaintyMeters),
            Math.Min(
                180.0,
                _settings.InitialYawUncertaintyDegrees
                    + Math.Max(
                        0.0,
                        currentPose.YawUncertaintyDegrees
                            - estimatedPoseAtCapture.YawUncertaintyDegrees)));

        CurrentPose = correctedPose;
        _velocityX = correctedVelocityX;
        _velocityZ = correctedVelocityZ;
        return correctedPose;
    }

    /// <summary>
    /// Applique une correction absolue partielle issue de la relocalisation
    /// visuelle, tout en conservant le mouvement effectué depuis la trame qui
    /// a fourni la preuve. La translation et le yaw sont bornés séparément.
    /// </summary>
    internal RealtimePose ApplyVisualRelocalizationCorrection(
        VisualKeyframePose exactPoseAtCapture,
        RealtimePose estimatedPoseAtCapture,
        double positionGain,
        double yawGain,
        double maximumTranslationStepMeters,
        double maximumYawStepDegrees)
    {
        if (CurrentPose is not { } currentPose)
        {
            throw new InvalidOperationException(
                "Une pose courante est requise pour corriger la relocalisation.");
        }

        if (!AreFinite(
                exactPoseAtCapture.X,
                exactPoseAtCapture.Y,
                exactPoseAtCapture.Z,
                exactPoseAtCapture.YawDegrees,
                estimatedPoseAtCapture.X,
                estimatedPoseAtCapture.Y,
                estimatedPoseAtCapture.Z,
                estimatedPoseAtCapture.YawDegrees,
                estimatedPoseAtCapture.PositionUncertaintyMeters,
                estimatedPoseAtCapture.YawUncertaintyDegrees)
            || positionGain is < 0.0 or > 1.0
            || yawGain is < 0.0 or > 1.0
            || double.IsNaN(maximumTranslationStepMeters)
            || maximumTranslationStepMeters <= 0.0
            || double.IsNaN(maximumYawStepDegrees)
            || maximumYawStepDegrees <= 0.0
            || estimatedPoseAtCapture.PositionUncertaintyMeters < 0.0
            || estimatedPoseAtCapture.YawUncertaintyDegrees < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionGain),
                "La correction visuelle contient des valeurs invalides.");
        }

        var anchorDeltaX = exactPoseAtCapture.X - estimatedPoseAtCapture.X;
        var anchorDeltaY = exactPoseAtCapture.Y - estimatedPoseAtCapture.Y;
        var anchorDeltaZ = exactPoseAtCapture.Z - estimatedPoseAtCapture.Z;
        var anchorDistance = Math.Sqrt(
            (anchorDeltaX * anchorDeltaX) + (anchorDeltaZ * anchorDeltaZ));
        var requestedTranslation = anchorDistance * positionGain;
        var appliedTranslation = Math.Min(
            requestedTranslation,
            maximumTranslationStepMeters);
        var translationFraction = anchorDistance <= double.Epsilon
            ? positionGain
            : Math.Clamp(appliedTranslation / anchorDistance, 0.0, 1.0);

        var fullYawCorrection = NormalizeSigned180(
            exactPoseAtCapture.YawDegrees - estimatedPoseAtCapture.YawDegrees);
        var requestedYawCorrection = fullYawCorrection * yawGain;
        var yawCorrection = Math.Clamp(
            requestedYawCorrection,
            -maximumYawStepDegrees,
            maximumYawStepDegrees);

        var correctionRadians = yawCorrection * DegreesToRadians;
        var cosine = Math.Cos(correctionRadians);
        var sine = Math.Sin(correctionRadians);

        var relativeX = currentPose.X - estimatedPoseAtCapture.X;
        var relativeY = currentPose.Y - estimatedPoseAtCapture.Y;
        var relativeZ = currentPose.Z - estimatedPoseAtCapture.Z;
        var correctedRelativeX = (relativeX * cosine) + (relativeZ * sine);
        var correctedRelativeZ = (-relativeX * sine) + (relativeZ * cosine);

        var correctedVelocityX = (_velocityX * cosine) + (_velocityZ * sine);
        var correctedVelocityZ = (-_velocityX * sine) + (_velocityZ * cosine);

        var correctedAnchorX =
            estimatedPoseAtCapture.X + (anchorDeltaX * translationFraction);
        var correctedAnchorY =
            estimatedPoseAtCapture.Y + (anchorDeltaY * translationFraction);
        var correctedAnchorZ =
            estimatedPoseAtCapture.Z + (anchorDeltaZ * translationFraction);

        var historicalPositionUncertainty = Math.Max(
            0.0,
            estimatedPoseAtCapture.PositionUncertaintyMeters
                - _settings.InitialPositionUncertaintyMeters);
        var historicalYawUncertainty = Math.Max(
            0.0,
            estimatedPoseAtCapture.YawUncertaintyDegrees
                - _settings.InitialYawUncertaintyDegrees);
        var yawFraction = Math.Abs(fullYawCorrection) <= double.Epsilon
            ? yawGain
            : Math.Clamp(
                Math.Abs(yawCorrection / fullYawCorrection),
                0.0,
                1.0);

        var corrected = new RealtimePose(
            correctedAnchorX + correctedRelativeX,
            correctedAnchorY + relativeY,
            correctedAnchorZ + correctedRelativeZ,
            Normalize360(currentPose.YawDegrees + yawCorrection),
            Math.Max(
                _settings.InitialPositionUncertaintyMeters,
                currentPose.PositionUncertaintyMeters
                    - (historicalPositionUncertainty * translationFraction)),
            Math.Max(
                _settings.InitialYawUncertaintyDegrees,
                currentPose.YawUncertaintyDegrees
                    - (historicalYawUncertainty * yawFraction)));

        CurrentPose = corrected;
        _velocityX = correctedVelocityX;
        _velocityZ = correctedVelocityZ;
        return corrected;
    }

    /// <summary>
    /// Avance l'estimation d'un pas. <paramref name="forward"/> et
    /// <paramref name="right"/> sont des axes compris entre -1 et 1.
    /// Un delta de souris positif tourne vers la droite.
    /// </summary>
    public RealtimePose Advance(
        double deltaTimeSeconds,
        double forward,
        double right,
        bool sprint,
        bool slow,
        double mouseDeltaCounts,
        bool aiming,
        double movementSpeedScale = 1.0,
        RealtimeMotionEvidence motionEvidence = RealtimeMotionEvidence.Unknown)
    {
        if (CurrentPose is not { } pose)
        {
            throw new InvalidOperationException("Une capture doit ancrer l'estimateur avant son utilisation.");
        }

        if (!AreFinite(deltaTimeSeconds, forward, right, mouseDeltaCounts, movementSpeedScale)
            || deltaTimeSeconds < 0.0
            || Math.Abs(forward) > 1.0
            || Math.Abs(right) > 1.0
            || movementSpeedScale is < 0.0 or > 1.0
            || !Enum.IsDefined(motionEvidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaTimeSeconds),
                "Le temps doit être positif et les axes doivent rester entre -1 et 1.");
        }

        var sensitivity = aiming
            ? _settings.AdsYawDegreesPerMouseCount
            : _settings.HipYawDegreesPerMouseCount;
        var yawDelta = mouseDeltaCounts * sensitivity;
        var nextYaw = Normalize360(pose.YawDegrees + yawDelta);

        // Le milieu du virage donne une meilleure approximation quand le joueur
        // tourne et avance pendant le même intervalle.
        var integrationYawRadians = Normalize360(pose.YawDegrees + (yawDelta * 0.5)) * DegreesToRadians;
        var inputMagnitude = Math.Sqrt((forward * forward) + (right * right));
        if (inputMagnitude > 1.0)
        {
            forward /= inputMagnitude;
            right /= inputMagnitude;
            inputMagnitude = 1.0;
        }

        var adjustedForward = forward * (forward < 0.0
            ? _settings.BackwardSpeedMultiplier
            : 1.0);
        var adjustedRight = right * _settings.StrafeSpeedMultiplier;

        var nominalSpeed = slow
            ? _settings.SlowSpeedMetersPerSecond
            : sprint
                ? _settings.SprintSpeedMetersPerSecond
                : _settings.WalkSpeedMetersPerSecond;
        var translationScale = motionEvidence == RealtimeMotionEvidence.Stationary
            ? 0.0
            : movementSpeedScale;

        var forwardX = Math.Sin(integrationYawRadians);
        var forwardZ = Math.Cos(integrationYawRadians);
        var rightX = Math.Cos(integrationYawRadians);
        var rightZ = -Math.Sin(integrationYawRadians);

        var targetVelocityScale = nominalSpeed * translationScale;
        var targetVelocityX = ((adjustedForward * forwardX) + (adjustedRight * rightX))
            * targetVelocityScale;
        var targetVelocityZ = ((adjustedForward * forwardZ) + (adjustedRight * rightZ))
            * targetVelocityScale;

        double deltaX;
        double deltaZ;
        if (motionEvidence == RealtimeMotionEvidence.Stationary)
        {
            // Une immobilité visuelle confirmée représente notamment une
            // collision. Elle annule aussi l'élan afin qu'il ne reparte pas au
            // tick suivant quand la touche vient d'être relâchée.
            _velocityX = 0.0;
            _velocityZ = 0.0;
            deltaX = 0.0;
            deltaZ = 0.0;
        }
        else
        {
            (deltaX, deltaZ, _velocityX, _velocityZ) = IntegrateVelocity(
                _velocityX,
                _velocityZ,
                targetVelocityX,
                targetVelocityZ,
                deltaTimeSeconds,
                _settings.AccelerationMetersPerSecondSquared,
                _settings.DecelerationMetersPerSecondSquared);
        }

        var distance = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));

        var headingErrorRadians = Math.Min(90.0, pose.YawUncertaintyDegrees) * DegreesToRadians;
        var uncertaintyFromHeading = distance * Math.Sin(headingErrorRadians);
        var unobservedMovementRate = motionEvidence switch
        {
            // Une différence entre deux images prouve seulement que l'image a
            // changé. Rotation, recul et animations peuvent produire le même
            // signal sans translation : Moving ne doit donc jamais inspirer
            // plus de confiance que l'absence de preuve visuelle.
            RealtimeMotionEvidence.Moving => Math.Max(
                _settings.UnobservedMovementUncertaintyMetersPerSecond,
                _settings.ConfirmedMovementUncertaintyMetersPerSecond),
            RealtimeMotionEvidence.Stationary => _settings.ConfirmedStationaryUncertaintyMetersPerSecond,
            _ => _settings.UnobservedMovementUncertaintyMetersPerSecond,
        };
        var movementActivity = inputMagnitude;
        if (movementActivity == 0.0 && distance > 0.0)
        {
            // Le freinage continue brièvement après le relâchement des touches.
            movementActivity = 1.0;
        }
        // ZUPT visuel conservateur : l'arrêt confirmé ne révèle pas la
        // position absolue, donc il ne réduit jamais l'incertitude historique.
        // En revanche, il supprime presque toute nouvelle dérive de translation
        // tant que la scène reste réellement immobile.
        var stationaryDriftScale =
            motionEvidence == RealtimeMotionEvidence.Stationary
                ? 0.10
                : 1.0;
        var positionUncertainty = pose.PositionUncertaintyMeters
            + (_settings.PositionDriftMetersPerSecond
                * deltaTimeSeconds
                * stationaryDriftScale)
            + (_settings.DistanceErrorFraction * distance)
            // Les touches indiquent une intention, pas un déplacement confirmé :
            // collision, terrain ou stamina peuvent annuler tout le pas estimé.
            + (unobservedMovementRate
                * deltaTimeSeconds
                * movementActivity)
            + uncertaintyFromHeading;
        var stationaryYawDriftScale =
            motionEvidence == RealtimeMotionEvidence.Stationary
                && Math.Abs(mouseDeltaCounts) < 0.5
                ? 0.25
                : 1.0;
        var yawUncertainty = Math.Min(
            180.0,
            pose.YawUncertaintyDegrees
            + (_settings.YawDriftDegreesPerSecond
                * deltaTimeSeconds
                * stationaryYawDriftScale)
            + (Math.Abs(yawDelta) * _settings.MouseYawErrorFraction));

        var nextPose = new RealtimePose(
            pose.X + deltaX,
            pose.Y,
            pose.Z + deltaZ,
            nextYaw,
            positionUncertainty,
            yawUncertainty);

        CurrentPose = nextPose;
        return nextPose;
    }

    /// <summary>Remove stale momentum after loss of control/focus. Missing time increases uncertainty, never confidence.</summary>
    public void SuspendMotion(double unobservedSeconds = 0)
    {
        if (!double.IsFinite(unobservedSeconds) || unobservedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(unobservedSeconds));
        _velocityX = _velocityZ = 0;
        if (CurrentPose is { } p && unobservedSeconds > 0)
        {
            var potentialTravel = Math.Min(300, unobservedSeconds)
                * Math.Max(_settings.SprintSpeedMetersPerSecond, Math.Max(_settings.WalkSpeedMetersPerSecond, _settings.SlowSpeedMetersPerSecond));
            CurrentPose = p with { PositionUncertaintyMeters = Math.Min(10000, p.PositionUncertaintyMeters + potentialTravel) };
        }
    }

    public void Reset()
    {
        CurrentPose = null;
        _velocityX = 0.0;
        _velocityZ = 0.0;
    }

    private static (double DeltaX, double DeltaZ, double VelocityX, double VelocityZ) IntegrateVelocity(
        double velocityX,
        double velocityZ,
        double targetVelocityX,
        double targetVelocityZ,
        double deltaTimeSeconds,
        double acceleration,
        double deceleration)
    {
        if (deltaTimeSeconds == 0.0)
        {
            return (0.0, 0.0, velocityX, velocityZ);
        }

        // An exact reversal has two physical phases: brake to zero, then accelerate
        // in the opposite direction. Keeping one deceleration rate through zero made
        // repeated forward/back taps depend on the size of the UI integration tick.
        var velocitySquared = velocityX * velocityX + velocityZ * velocityZ;
        var targetSquared = targetVelocityX * targetVelocityX + targetVelocityZ * targetVelocityZ;
        var dot = velocityX * targetVelocityX + velocityZ * targetVelocityZ;
        var cross = velocityX * targetVelocityZ - velocityZ * targetVelocityX;
        if (dot < 0 && deceleration > double.Epsilon
            && cross * cross <= 1e-16 * velocitySquared * targetSquared)
        {
            var stopTime = Math.Sqrt(velocitySquared) / deceleration;
            var braking = IntegrateVelocity(velocityX, velocityZ, 0, 0,
                Math.Min(deltaTimeSeconds, stopTime), acceleration, deceleration);
            if (deltaTimeSeconds <= stopTime) return braking;
            var launch = IntegrateVelocity(0, 0, targetVelocityX, targetVelocityZ,
                deltaTimeSeconds - stopTime, acceleration, deceleration);
            return (braking.DeltaX + launch.DeltaX, braking.DeltaZ + launch.DeltaZ,
                launch.VelocityX, launch.VelocityZ);
        }

        var differenceX = targetVelocityX - velocityX;
        var differenceZ = targetVelocityZ - velocityZ;
        var differenceMagnitude = Math.Sqrt((differenceX * differenceX) + (differenceZ * differenceZ));
        if (differenceMagnitude <= double.Epsilon)
        {
            return (
                velocityX * deltaTimeSeconds,
                velocityZ * deltaTimeSeconds,
                velocityX,
                velocityZ);
        }

        var currentSpeed = Math.Sqrt((velocityX * velocityX) + (velocityZ * velocityZ));
        var targetSpeed = Math.Sqrt(
            (targetVelocityX * targetVelocityX) + (targetVelocityZ * targetVelocityZ));
        var sameGeneralDirection = currentSpeed <= double.Epsilon
            || targetSpeed <= double.Epsilon
            || ((velocityX * targetVelocityX) + (velocityZ * targetVelocityZ)) >= 0.0;
        var rate = targetSpeed > currentSpeed && sameGeneralDirection
            ? acceleration
            : deceleration;

        if (rate <= double.Epsilon)
        {
            return (
                velocityX * deltaTimeSeconds,
                velocityZ * deltaTimeSeconds,
                velocityX,
                velocityZ);
        }

        var timeToTarget = differenceMagnitude / rate;
        if (timeToTarget >= deltaTimeSeconds)
        {
            var ratio = (rate * deltaTimeSeconds) / differenceMagnitude;
            var nextVelocityX = velocityX + (differenceX * ratio);
            var nextVelocityZ = velocityZ + (differenceZ * ratio);
            return (
                (velocityX + nextVelocityX) * 0.5 * deltaTimeSeconds,
                (velocityZ + nextVelocityZ) * 0.5 * deltaTimeSeconds,
                nextVelocityX,
                nextVelocityZ);
        }

        // Atteindre la cible avant la fin du tick : intégrer exactement la
        // rampe, puis la portion restante à vitesse constante.
        var deltaX = ((velocityX + targetVelocityX) * 0.5 * timeToTarget)
            + (targetVelocityX * (deltaTimeSeconds - timeToTarget));
        var deltaZ = ((velocityZ + targetVelocityZ) * 0.5 * timeToTarget)
            + (targetVelocityZ * (deltaTimeSeconds - timeToTarget));
        return (deltaX, deltaZ, targetVelocityX, targetVelocityZ);
    }

    private static void ValidateSettings(RealtimePoseEstimatorSettings settings)
    {
        var nonNegativeValues = new[]
        {
            settings.HipYawDegreesPerMouseCount,
            settings.AdsYawDegreesPerMouseCount,
            settings.WalkSpeedMetersPerSecond,
            settings.SprintSpeedMetersPerSecond,
            settings.SlowSpeedMetersPerSecond,
            settings.BackwardSpeedMultiplier,
            settings.StrafeSpeedMultiplier,
            settings.AccelerationMetersPerSecondSquared,
            settings.DecelerationMetersPerSecondSquared,
            settings.InitialPositionUncertaintyMeters,
            settings.InitialYawUncertaintyDegrees,
            settings.PositionDriftMetersPerSecond,
            settings.DistanceErrorFraction,
            settings.UnobservedMovementUncertaintyMetersPerSecond,
            settings.YawDriftDegreesPerSecond,
            settings.MouseYawErrorFraction,
            settings.ConfirmedMovementUncertaintyMetersPerSecond,
            settings.ConfirmedStationaryUncertaintyMetersPerSecond
        };

        if (nonNegativeValues.Any(value => !double.IsFinite(value) || value < 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Tous les paramètres doivent être finis et positifs ou nuls.");
        }


        if (settings.BackwardSpeedMultiplier > 1.0
            || settings.StrafeSpeedMultiplier > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Les multiplicateurs directionnels doivent rester entre zéro et un.");
        }
    }

    private static bool AreFinite(params double[] values)
    {
        return values.All(double.IsFinite);
    }

    private static double Normalize360(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeSigned180(double degrees)
    {
        var normalized = Normalize360(degrees);
        return normalized > 180.0 ? normalized - 360.0 : normalized;
    }
}