using System.Diagnostics;
using System.Numerics;

namespace RaidPilot.Overlay;

internal readonly record struct VisualKeyframePose(
    double X,
    double Y,
    double Z,
    double YawDegrees);

internal enum VisualKeyframeRecognitionState
{
    NoMatch,
    Candidate,
    Confirmed,
    Cooldown,
}

/// <summary>
/// Résultat prudent de reconnaissance d'un lieu déjà ancré par une capture EFT.
/// Une pose n'est fournie qu'après plusieurs observations consécutives du même
/// repère; un candidat isolé ne doit jamais recaler le joueur.
/// </summary>
internal readonly record struct VisualKeyframeRecognition(
    VisualKeyframeRecognitionState State,
    long? KeyframeId,
    VisualKeyframePose? ExactPose,
    int ConsecutiveMatches,
    double DescriptorDistance,
    double UniquenessMargin,
    int ComparedBits)
{
    internal bool IsConfirmed => State == VisualKeyframeRecognitionState.Confirmed
        && KeyframeId.HasValue
        && ExactPose.HasValue;

    internal static VisualKeyframeRecognition NoMatch() => new(
        VisualKeyframeRecognitionState.NoMatch,
        null,
        null,
        0,
        1,
        0,
        0);

    internal static VisualKeyframeRecognition Cooldown() => new(
        VisualKeyframeRecognitionState.Cooldown,
        null,
        null,
        0,
        1,
        0,
        0);
}

internal sealed record VisualKeyframeRecognizerSettings
{
    internal int FrameWidth { get; init; } = 64;

    internal int FrameHeight { get; init; } = 36;

    internal int GridColumns { get; init; } = 10;

    internal int GridRows { get; init; } = 6;

    internal int CensusRadius { get; init; } = 2;

    internal int AlignmentSearchRadius { get; init; } = 2;

    internal int MinimumCensusContrast { get; init; } = 7;

    internal int MinimumComparedBits { get; init; } = 120;

    internal double MinimumComparedBitRatio { get; init; } = 0.62;

    internal double MinimumTextureStandardDeviation { get; init; } = 11.0;

    internal double MaximumDescriptorDistance { get; init; } = 0.115;

    internal double MinimumBestSecondMargin { get; init; } = 0.045;

    internal double MaximumEstimatedDistanceMeters { get; init; } = 35.0;

    internal double MaximumVerticalDistanceMeters { get; init; } = 9.0;

    internal double MaximumEstimatedYawDifferenceDegrees { get; init; } = 25.0;

    internal int RequiredConsecutiveMatches { get; init; } = 3;

    internal TimeSpan MinimumConfirmationInterval { get; init; } = TimeSpan.FromMilliseconds(80);

    internal TimeSpan MaximumConfirmationGap { get; init; } = TimeSpan.FromMilliseconds(500);

    internal TimeSpan ConfirmationCooldown { get; init; } = TimeSpan.FromSeconds(8);

    internal int MaximumKeyframesPerMap { get; init; } = 64;

    internal int MaximumMaps { get; init; } = 16;

    // Les trames de VisualMotionGate sont déjà recadrées au centre de l'écran,
    // mais ces masques secondaires empêchent encore le HUD, la barre basse et
    // l'arme de devenir des repères artificiellement stables.
    internal double SideHudMaskFraction { get; init; } = 0.06;

    internal double TopHudMaskFraction { get; init; } = 0.10;

    internal double BottomHudMaskFraction { get; init; } = 0.08;

    internal double WeaponMaskTopFraction { get; init; } = 0.64;

    internal double WeaponMaskLeftFraction { get; init; } = 0.24;

    internal double WeaponMaskRightFraction { get; init; } = 0.78;
}

/// <summary>
/// Reconnaissance locale et sans dépendance de repères visuels. Seules des
/// signatures Census compactes sont gardées en mémoire: aucune capture, aucun
/// texte et aucune donnée de jeu ne sont écrits sur disque.
///
/// Le descripteur compare des pixels voisins plutôt que leur luminosité brute,
/// ce qui le rend insensible à un petit changement global d'exposition. Une
/// recherche de quelques pixels absorbe les minuscules rotations entre deux
/// observations. La proximité de la pose estimée, le cap, une marge face au
/// deuxième meilleur résultat et trois confirmations consécutives rendent le
/// recalage volontairement difficile à déclencher par erreur.
/// </summary>
internal sealed class VisualKeyframeRecognizer
{
    private static readonly (int AX, int AY, int BX, int BY)[] CensusPairs =
    [
        (-2, 0, 2, 0),
        (0, -2, 0, 2),
        (-2, -2, 2, 2),
        (2, -2, -2, 2),
        (-2, -1, 2, 1),
        (-2, 1, 2, -1),
        (-1, -2, 1, 2),
        (1, -2, -1, 2),
        (-2, 0, 0, -2),
        (0, -2, 2, 0),
        (2, 0, 0, 2),
        (0, 2, -2, 0),
    ];

    private readonly object _sync = new();
    private readonly VisualKeyframeRecognizerSettings _settings;
    private readonly Dictionary<string, MapKeyframes> _maps = new(StringComparer.Ordinal);

    private long _nextKeyframeId = 1;
    private long _touchSequence;
    private long _pendingKeyframeId;
    private int _pendingMatches;
    private long _pendingLastMatchTimestamp;
    private long _cooldownUntilTimestamp;
    private long _lastObservationTimestamp;

    internal VisualKeyframeRecognizer(VisualKeyframeRecognizerSettings? settings = null)
    {
        _settings = settings ?? new VisualKeyframeRecognizerSettings();
        ValidateSettings(_settings);
    }

    internal int MapCount
    {
        get
        {
            lock (_sync)
            {
                return _maps.Count;
            }
        }
    }

    /// <summary>
    /// Ajoute une trame associée à une pose absolue fiable. Une scène sans
    /// texture n'est pas mémorisée. Les doublons pris pratiquement au même
    /// endroit remplacent l'ancien repère plutôt que de rendre le résultat
    /// ambigu.
    /// </summary>
    internal bool AddExactKeyframe(
        string mapId,
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        VisualKeyframePose exactPose,
        long timestamp,
        out long keyframeId)
    {
        lock (_sync)
        {
            var normalizedMapId = NormalizeMapId(mapId);
            ValidateFrame(luminance, width, height);
            ValidatePose(exactPose, nameof(exactPose));
            if (timestamp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            var descriptor = CreateDescriptor(luminance, width, height, 0, 0);
            if (!descriptor.IsUsable(_settings))
            {
                keyframeId = 0;
                return false;
            }

            var map = GetOrCreateMap(normalizedMapId);
            map.LastTouchedSequence = ++_touchSequence;

            // Deux ancres quasi identiques ne doivent pas s'affronter comme
            // meilleur et deuxième meilleur résultat lors de l'observation.
            for (var index = 0; index < map.Keyframes.Count; index++)
            {
                var existing = map.Keyframes[index];
                if (SpatialDistance(existing.Pose, exactPose) <= 2.0
                    && AngleDistance(existing.Pose.YawDegrees, exactPose.YawDegrees) <= 5.0
                    && TryCompare(existing.Descriptor, descriptor, out var duplicateDistance, out _)
                    && duplicateDistance <= _settings.MaximumDescriptorDistance / 2.0)
                {
                    map.Keyframes[index] = existing with
                    {
                        Pose = exactPose with { YawDegrees = Normalize360(exactPose.YawDegrees) },
                        Descriptor = descriptor,
                        CapturedAtTimestamp = timestamp,
                    };
                    keyframeId = existing.Id;
                    return true;
                }
            }

            if (map.Keyframes.Count >= _settings.MaximumKeyframesPerMap)
            {
                var oldestIndex = 0;
                var oldestTimestamp = map.Keyframes[0].CapturedAtTimestamp;
                for (var index = 1; index < map.Keyframes.Count; index++)
                {
                    if (map.Keyframes[index].CapturedAtTimestamp < oldestTimestamp)
                    {
                        oldestIndex = index;
                        oldestTimestamp = map.Keyframes[index].CapturedAtTimestamp;
                    }
                }

                var removedId = map.Keyframes[oldestIndex].Id;
                map.Keyframes.RemoveAt(oldestIndex);
                if (_pendingKeyframeId == removedId)
                {
                    ClearPending();
                }
            }

            keyframeId = _nextKeyframeId++;
            map.Keyframes.Add(new Keyframe(
                keyframeId,
                exactPose with { YawDegrees = Normalize360(exactPose.YawDegrees) },
                descriptor,
                timestamp));
            return true;
        }
    }

    internal VisualKeyframeRecognition Observe(
        string mapId,
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        VisualKeyframePose estimatedPose,
        long timestamp)
    {
        lock (_sync)
        {
            var normalizedMapId = NormalizeMapId(mapId);
            ValidateFrame(luminance, width, height);
            ValidatePose(estimatedPose, nameof(estimatedPose));
            if (timestamp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            // Un producteur peut livrer deux trames dans le même tick. Une
            // valeur régressive est bornée afin de ne jamais contourner le
            // cooldown avec une horloge mal ordonnée.
            timestamp = Math.Max(timestamp, _lastObservationTimestamp);
            _lastObservationTimestamp = timestamp;

            if (timestamp < _cooldownUntilTimestamp)
            {
                ClearPending();
                return VisualKeyframeRecognition.Cooldown();
            }

            if (!_maps.TryGetValue(normalizedMapId, out var map)
                || map.Keyframes.Count == 0)
            {
                ClearPending();
                return VisualKeyframeRecognition.NoMatch();
            }

            map.LastTouchedSequence = ++_touchSequence;
            var descriptors = CreateAlignedDescriptors(luminance, width, height);
            if (descriptors.Count == 0)
            {
                ClearPending();
                return VisualKeyframeRecognition.NoMatch();
            }

            MatchCandidate? best = null;
            MatchCandidate? second = null;

            // Mesurer d'abord l'ambiguïté sur toute la carte. Filtrer les
            // repères par la pose avant la comparaison ferait disparaître un
            // sosie situé juste hors du rayon de recherche et lui donnerait une
            // fausse marge d'unicité.
            foreach (var keyframe in map.Keyframes)
            {
                MatchCandidate? keyframeBest = null;
                foreach (var descriptor in descriptors)
                {
                    if (!TryCompare(
                            keyframe.Descriptor,
                            descriptor,
                            out var distance,
                            out var comparedBits))
                    {
                        continue;
                    }

                    if (keyframeBest is null || distance < keyframeBest.Value.Distance)
                    {
                        keyframeBest = new MatchCandidate(keyframe, distance, comparedBits);
                    }
                }

                if (keyframeBest is not { } candidate)
                {
                    continue;
                }

                if (best is null || candidate.Distance < best.Value.Distance)
                {
                    second = best;
                    best = candidate;
                }
                else if (second is null || candidate.Distance < second.Value.Distance)
                {
                    second = candidate;
                }
            }

            if (best is not { } winner
                || winner.Distance > _settings.MaximumDescriptorDistance)
            {
                ClearPending();
                return VisualKeyframeRecognition.NoMatch();
            }

            var uniquenessMargin = second is { } runnerUp
                ? runnerUp.Distance - winner.Distance
                : 1.0 - winner.Distance;
            if (uniquenessMargin < _settings.MinimumBestSecondMargin)
            {
                ClearPending();
                return VisualKeyframeRecognition.NoMatch();
            }

            if (SpatialDistance(winner.Keyframe.Pose, estimatedPose)
                    > _settings.MaximumEstimatedDistanceMeters
                || Math.Abs(winner.Keyframe.Pose.Y - estimatedPose.Y)
                    > _settings.MaximumVerticalDistanceMeters
                || AngleDistance(winner.Keyframe.Pose.YawDegrees, estimatedPose.YawDegrees)
                    > _settings.MaximumEstimatedYawDifferenceDegrees)
            {
                ClearPending();
                return VisualKeyframeRecognition.NoMatch();
            }

            if (_pendingKeyframeId == winner.Keyframe.Id)
            {
                var elapsed = timestamp - _pendingLastMatchTimestamp;
                var minimumTicks = SecondsToTimestampTicks(
                    _settings.MinimumConfirmationInterval.TotalSeconds);
                var maximumTicks = SecondsToTimestampTicks(
                    _settings.MaximumConfirmationGap.TotalSeconds);
                if (elapsed < minimumTicks)
                {
                    return Candidate(winner, uniquenessMargin, _pendingMatches);
                }

                _pendingMatches = elapsed > maximumTicks
                    ? 1
                    : _pendingMatches + 1;
                _pendingLastMatchTimestamp = timestamp;
            }
            else
            {
                _pendingKeyframeId = winner.Keyframe.Id;
                _pendingMatches = 1;
                _pendingLastMatchTimestamp = timestamp;
            }

            if (_pendingMatches < _settings.RequiredConsecutiveMatches)
            {
                return Candidate(winner, uniquenessMargin, _pendingMatches);
            }

            var pose = winner.Keyframe.Pose;
            var confirmedCount = _pendingMatches;
            var cooldownTicks = SecondsToTimestampTicks(_settings.ConfirmationCooldown.TotalSeconds);
            _cooldownUntilTimestamp = SaturatingAdd(timestamp, cooldownTicks);
            ClearPending();
            return new VisualKeyframeRecognition(
                VisualKeyframeRecognitionState.Confirmed,
                winner.Keyframe.Id,
                pose,
                confirmedCount,
                winner.Distance,
                uniquenessMargin,
                winner.ComparedBits);
        }
    }

    /// <summary>
    /// Oublie uniquement l'état temporel du raid. Les repères exacts restent en
    /// mémoire et peuvent être réutilisés lors d'une partie suivante.
    /// </summary>
    internal void ResetSession()
    {
        lock (_sync)
        {
            ClearPending();
            _cooldownUntilTimestamp = 0;
            _lastObservationTimestamp = 0;
        }
    }

    internal void CancelPendingMatch()
    {
        lock (_sync)
        {
            ClearPending();
        }
    }

    internal int KeyframeCountForMap(string mapId)
    {
        lock (_sync)
        {
            var normalizedMapId = NormalizeMapId(mapId);
            return _maps.TryGetValue(normalizedMapId, out var map)
                ? map.Keyframes.Count
                : 0;
        }
    }

    private List<FrameDescriptor> CreateAlignedDescriptors(
        ReadOnlySpan<byte> luminance,
        int width,
        int height)
    {
        var diameter = (_settings.AlignmentSearchRadius * 2) + 1;
        var descriptors = new List<FrameDescriptor>(diameter * diameter);
        for (var dy = -_settings.AlignmentSearchRadius;
             dy <= _settings.AlignmentSearchRadius;
             dy++)
        {
            for (var dx = -_settings.AlignmentSearchRadius;
                 dx <= _settings.AlignmentSearchRadius;
                 dx++)
            {
                var descriptor = CreateDescriptor(luminance, width, height, dx, dy);
                if (descriptor.IsUsable(_settings))
                {
                    descriptors.Add(descriptor);
                }
            }
        }

        return descriptors;
    }

    private FrameDescriptor CreateDescriptor(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        int shiftX,
        int shiftY)
    {
        var bitCapacity = checked(_settings.GridColumns * _settings.GridRows * CensusPairs.Length);
        var wordCount = (bitCapacity + 63) / 64;
        var bits = new ulong[wordCount];
        var validBits = new ulong[wordCount];
        var bitIndex = 0;

        var textureCount = 0;
        var textureMean = 0.0;
        var textureM2 = 0.0;

        var margin = _settings.CensusRadius + _settings.AlignmentSearchRadius;
        var usableWidth = width - (margin * 2);
        var usableHeight = height - (margin * 2);

        for (var gridY = 0; gridY < _settings.GridRows; gridY++)
        {
            var baseY = SampleCoordinate(margin, usableHeight, gridY, _settings.GridRows);
            for (var gridX = 0; gridX < _settings.GridColumns; gridX++)
            {
                var baseX = SampleCoordinate(margin, usableWidth, gridX, _settings.GridColumns);
                var x = baseX + shiftX;
                var y = baseY + shiftY;
                var sceneSample = IsSceneSample(x, y, width, height);

                if (sceneSample)
                {
                    textureCount++;
                    var sample = luminance[(y * width) + x];
                    var delta = sample - textureMean;
                    textureMean += delta / textureCount;
                    textureM2 += delta * (sample - textureMean);
                }

                foreach (var pair in CensusPairs)
                {
                    if (sceneSample)
                    {
                        var a = luminance[((y + pair.AY) * width) + x + pair.AX];
                        var b = luminance[((y + pair.BY) * width) + x + pair.BX];
                        var contrast = Math.Abs(a - b);
                        if (contrast >= _settings.MinimumCensusContrast)
                        {
                            SetBit(validBits, bitIndex);
                            if (a > b)
                            {
                                SetBit(bits, bitIndex);
                            }
                        }
                    }

                    bitIndex++;
                }
            }
        }

        var validBitCount = CountBits(validBits);
        var textureStandardDeviation = textureCount > 1
            ? Math.Sqrt(textureM2 / textureCount)
            : 0;
        return new FrameDescriptor(bits, validBits, validBitCount, textureStandardDeviation);
    }

    private bool TryCompare(
        FrameDescriptor reference,
        FrameDescriptor candidate,
        out double distance,
        out int comparedBits)
    {
        var differentBits = 0;
        comparedBits = 0;
        for (var index = 0; index < reference.Bits.Length; index++)
        {
            var commonMask = reference.ValidBits[index] & candidate.ValidBits[index];
            comparedBits += BitOperations.PopCount(commonMask);
            differentBits += BitOperations.PopCount(
                (reference.Bits[index] ^ candidate.Bits[index]) & commonMask);
        }

        var minimumRatioBits = (int)Math.Ceiling(
            reference.ValidBitCount * _settings.MinimumComparedBitRatio);
        if (comparedBits < _settings.MinimumComparedBits
            || comparedBits < minimumRatioBits)
        {
            distance = 1;
            return false;
        }

        distance = differentBits / (double)comparedBits;
        return true;
    }

    private bool IsSceneSample(int centerX, int centerY, int width, int height)
    {
        var radius = _settings.CensusRadius;
        if (centerX - radius < 0
            || centerX + radius >= width
            || centerY - radius < 0
            || centerY + radius >= height)
        {
            return false;
        }

        var left = (centerX - radius) / (double)width;
        var right = (centerX + radius + 1) / (double)width;
        var top = (centerY - radius) / (double)height;
        var bottom = (centerY + radius + 1) / (double)height;

        if (left < _settings.SideHudMaskFraction
            || right > 1.0 - _settings.SideHudMaskFraction
            || top < _settings.TopHudMaskFraction
            || bottom > 1.0 - _settings.BottomHudMaskFraction)
        {
            return false;
        }

        var intersectsWeapon = bottom > _settings.WeaponMaskTopFraction
            && right > _settings.WeaponMaskLeftFraction
            && left < _settings.WeaponMaskRightFraction;
        return !intersectsWeapon;
    }

    private MapKeyframes GetOrCreateMap(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var existing))
        {
            return existing;
        }

        if (_maps.Count >= _settings.MaximumMaps)
        {
            var oldest = _maps.Aggregate((left, right) =>
                left.Value.LastTouchedSequence <= right.Value.LastTouchedSequence
                    ? left
                    : right);
            foreach (var removed in oldest.Value.Keyframes)
            {
                if (_pendingKeyframeId == removed.Id)
                {
                    ClearPending();
                    break;
                }
            }

            _maps.Remove(oldest.Key);
        }

        var created = new MapKeyframes { LastTouchedSequence = ++_touchSequence };
        _maps.Add(mapId, created);
        return created;
    }

    private void ClearPending()
    {
        _pendingKeyframeId = 0;
        _pendingMatches = 0;
        _pendingLastMatchTimestamp = 0;
    }

    private static VisualKeyframeRecognition Candidate(
        MatchCandidate winner,
        double uniquenessMargin,
        int consecutiveMatches) => new(
            VisualKeyframeRecognitionState.Candidate,
            winner.Keyframe.Id,
            null,
            consecutiveMatches,
            winner.Distance,
            uniquenessMargin,
            winner.ComparedBits);

    private static int SampleCoordinate(int start, int length, int index, int count)
    {
        if (count == 1)
        {
            return start + (length / 2);
        }

        return start + (int)Math.Round(index * (length - 1.0) / (count - 1.0));
    }

    private static void SetBit(ulong[] words, int bitIndex)
    {
        words[bitIndex >> 6] |= 1UL << (bitIndex & 63);
    }

    private static int CountBits(ulong[] words)
    {
        var count = 0;
        foreach (var word in words)
        {
            count += BitOperations.PopCount(word);
        }

        return count;
    }

    private static double SpatialDistance(VisualKeyframePose left, VisualKeyframePose right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double AngleDistance(double left, double right)
    {
        var difference = Math.Abs(Normalize360(left) - Normalize360(right));
        return Math.Min(difference, 360.0 - difference);
    }

    private static double Normalize360(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static long SecondsToTimestampTicks(double seconds)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        var ticks = seconds * Stopwatch.Frequency;
        return ticks >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(ticks);
    }

    private static long SaturatingAdd(long left, long right) =>
        long.MaxValue - left < right ? long.MaxValue : left + right;

    private void ValidateFrame(ReadOnlySpan<byte> luminance, int width, int height)
    {
        if (width != _settings.FrameWidth || height != _settings.FrameHeight)
        {
            throw new ArgumentException(
                $"La trame attendue mesure {_settings.FrameWidth}x{_settings.FrameHeight}.",
                nameof(luminance));
        }

        if (luminance.Length != checked(width * height))
        {
            throw new ArgumentException("La taille de la trame de luminance est invalide.", nameof(luminance));
        }
    }

    private static string NormalizeMapId(string mapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        return mapId.Trim().ToLowerInvariant();
    }

    private static void ValidatePose(VisualKeyframePose pose, string parameterName)
    {
        if (!double.IsFinite(pose.X)
            || !double.IsFinite(pose.Y)
            || !double.IsFinite(pose.Z)
            || !double.IsFinite(pose.YawDegrees))
        {
            throw new ArgumentException("La pose doit contenir uniquement des valeurs finies.", parameterName);
        }
    }

    private static void ValidateSettings(VisualKeyframeRecognizerSettings settings)
    {
        if (settings.FrameWidth < 24
            || settings.FrameHeight < 18
            || settings.GridColumns < 3
            || settings.GridRows < 3
            || settings.CensusRadius != 2
            || settings.AlignmentSearchRadius is < 0 or > 4
            || settings.MinimumCensusContrast is < 1 or > 64
            || settings.MinimumComparedBits < 16
            || !IsUnitFraction(settings.MinimumComparedBitRatio)
            || !double.IsFinite(settings.MinimumTextureStandardDeviation)
            || settings.MinimumTextureStandardDeviation <= 0
            || !IsUnitFraction(settings.MaximumDescriptorDistance)
            || settings.MaximumDescriptorDistance <= 0
            || !IsUnitFraction(settings.MinimumBestSecondMargin)
            || settings.MinimumBestSecondMargin <= 0
            || !double.IsFinite(settings.MaximumEstimatedDistanceMeters)
            || settings.MaximumEstimatedDistanceMeters <= 0
            || !double.IsFinite(settings.MaximumVerticalDistanceMeters)
            || settings.MaximumVerticalDistanceMeters <= 0
            || !double.IsFinite(settings.MaximumEstimatedYawDifferenceDegrees)
            || settings.MaximumEstimatedYawDifferenceDegrees is <= 0 or > 180
            || settings.RequiredConsecutiveMatches < 2
            || settings.MinimumConfirmationInterval < TimeSpan.Zero
            || settings.MaximumConfirmationGap <= settings.MinimumConfirmationInterval
            || settings.ConfirmationCooldown < TimeSpan.Zero
            || settings.MaximumKeyframesPerMap is < 1 or > 64
            || settings.MaximumMaps is < 1 or > 16
            || !IsUnitFraction(settings.SideHudMaskFraction)
            || !IsUnitFraction(settings.TopHudMaskFraction)
            || !IsUnitFraction(settings.BottomHudMaskFraction)
            || !IsUnitFraction(settings.WeaponMaskTopFraction)
            || !IsUnitFraction(settings.WeaponMaskLeftFraction)
            || !IsUnitFraction(settings.WeaponMaskRightFraction)
            || settings.WeaponMaskLeftFraction >= settings.WeaponMaskRightFraction
            || settings.TopHudMaskFraction + settings.BottomHudMaskFraction >= 0.75)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var margin = settings.CensusRadius + settings.AlignmentSearchRadius;
        if (settings.FrameWidth - (2 * margin) < settings.GridColumns
            || settings.FrameHeight - (2 * margin) < settings.GridRows)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "La grille ne tient pas dans la trame.");
        }

        var bitCapacity = checked(settings.GridColumns * settings.GridRows * CensusPairs.Length);
        if (settings.MinimumComparedBits > bitCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Le minimum de bits dépasse le descripteur.");
        }
    }

    private static bool IsUnitFraction(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    private sealed class MapKeyframes
    {
        internal List<Keyframe> Keyframes { get; } = [];

        internal long LastTouchedSequence { get; set; }
    }

    private sealed record Keyframe(
        long Id,
        VisualKeyframePose Pose,
        FrameDescriptor Descriptor,
        long CapturedAtTimestamp);

    private sealed record FrameDescriptor(
        ulong[] Bits,
        ulong[] ValidBits,
        int ValidBitCount,
        double TextureStandardDeviation)
    {
        internal bool IsUsable(VisualKeyframeRecognizerSettings settings) =>
            ValidBitCount >= settings.MinimumComparedBits
            && TextureStandardDeviation >= settings.MinimumTextureStandardDeviation;
    }

    private readonly record struct MatchCandidate(
        Keyframe Keyframe,
        double Distance,
        int ComparedBits);
}
