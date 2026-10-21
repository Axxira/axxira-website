using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace RaidPilot.Overlay;

internal enum SuperKeyframeRecognitionState
{
    NoMatch,
    Candidate,
    Confirmed,
    Cooldown,
}

internal readonly record struct SuperKeyframeRecognition(
    SuperKeyframeRecognitionState State,
    long? KeyframeId,
    VisualKeyframePose? ExactPose,
    int ConsecutiveMatches,
    double CompositeDistance,
    double UniquenessMargin,
    double RegionalConsensus,
    int ComparedBits,
    bool GlobalLoopClosure)
{
    internal bool IsConfirmed =>
        State == SuperKeyframeRecognitionState.Confirmed
        && KeyframeId.HasValue
        && ExactPose.HasValue;

    internal static SuperKeyframeRecognition NoMatch() => new(
        SuperKeyframeRecognitionState.NoMatch,
        null,
        null,
        0,
        1,
        0,
        0,
        0,
        false);

    internal static SuperKeyframeRecognition Cooldown() => new(
        SuperKeyframeRecognitionState.Cooldown,
        null,
        null,
        0,
        1,
        0,
        0,
        0,
        false);
}

internal sealed record SuperKeyframeDatabaseSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public List<SuperKeyframeMapSnapshot> Maps { get; init; } = [];
}

internal sealed record SuperKeyframeMapSnapshot
{
    public string MapId { get; init; } = string.Empty;
    public List<SuperKeyframeEntrySnapshot> Keyframes { get; init; } = [];
}

internal sealed record SuperKeyframeEntrySnapshot
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double YawDegrees { get; init; }
    public string GlobalBase64 { get; init; } = string.Empty;
    public string GlobalValidBase64 { get; init; } = string.Empty;
    public string GradientBase64 { get; init; } = string.Empty;
    public string CensusBitsBase64 { get; init; } = string.Empty;
    public string CensusValidBase64 { get; init; } = string.Empty;
    public string RegionHashesBase64 { get; init; } = string.Empty;
    public string RegionValidBase64 { get; init; } = string.Empty;
    public int ValidBitCount { get; init; }
    public double TextureStandardDeviation { get; init; }
    public double GradientEnergy { get; init; }
    public double MeanLuminance { get; init; }
}

internal sealed record SuperKeyframeRecognizerSettings
{
    internal int FrameWidth { get; init; } = HighResolutionVisualSampler.SampleWidth;
    internal int FrameHeight { get; init; } = HighResolutionVisualSampler.SampleHeight;

    internal int GlobalColumns { get; init; } = 12;
    internal int GlobalRows { get; init; } = 7;

    internal int CensusColumns { get; init; } = 16;
    internal int CensusRows { get; init; } = 9;
    internal int CensusRadius { get; init; } = 3;
    internal int MinimumCensusContrast { get; init; } = 6;
    internal int MinimumComparedBits { get; init; } = 720;

    internal int GradientColumns { get; init; } = 8;
    internal int GradientRows { get; init; } = 5;
    internal int GradientBins { get; init; } = 8;

    internal int RegionColumns { get; init; } = 5;
    internal int RegionRows { get; init; } = 3;
    internal int MinimumRegionComparedBits { get; init; } = 28;
    internal double MaximumRegionHammingDistance { get; init; } = 0.30;

    internal double MinimumTextureStandardDeviation { get; init; } = 10.0;
    internal double MinimumGradientEnergy { get; init; } = 5.0;

    internal double MaximumCompositeDistance { get; init; } = 0.27;
    internal double MinimumBestSecondMargin { get; init; } = 0.025;
    internal double MinimumRegionalConsensus { get; init; } = 0.46;

    internal double MaximumEstimatedDistanceMeters { get; init; } = 45.0;
    internal double MaximumVerticalDistanceMeters { get; init; } = 10.0;
    internal double MaximumEstimatedYawDifferenceDegrees { get; init; } = 40.0;

    internal double GlobalLoopClosureMinimumUncertaintyMeters { get; init; } = 4.0;
    internal double GlobalMaximumCompositeDistance { get; init; } = 0.19;
    internal double GlobalMinimumBestSecondMargin { get; init; } = 0.045;
    internal double GlobalMinimumRegionalConsensus { get; init; } = 0.67;
    internal double GlobalMaximumVerticalDistanceMeters { get; init; } = 18.0;
    internal double GlobalMaximumEstimatedYawDifferenceDegrees { get; init; } = 110.0;

    internal int RequiredConsecutiveMatches { get; init; } = 2;
    internal int GlobalRequiredConsecutiveMatches { get; init; } = 3;
    internal TimeSpan MinimumConfirmationInterval { get; init; } =
        TimeSpan.FromMilliseconds(90);
    internal TimeSpan MaximumConfirmationGap { get; init; } =
        TimeSpan.FromMilliseconds(700);
    internal TimeSpan ConfirmationCooldown { get; init; } =
        TimeSpan.FromSeconds(10);

    internal int MaximumKeyframesPerMap { get; init; } = 96;
    internal int MaximumMaps { get; init; } = 16;

    internal double SideHudMaskFraction { get; init; } = 0.05;
    internal double TopHudMaskFraction { get; init; } = 0.08;
    internal double BottomHudMaskFraction { get; init; } = 0.08;
    internal double WeaponMaskTopFraction { get; init; } = 0.64;
    internal double WeaponMaskLeftFraction { get; init; } = 0.22;
    internal double WeaponMaskRightFraction { get; init; } = 0.80;
}

/// <summary>
/// Vérification visuelle haute résolution inspirée des pipelines VPR/SLAM
/// hiérarchiques : signature globale normalisée pour le retrieval, Census et
/// histogrammes de gradients pour la structure locale, hashes régionaux pour
/// exiger un consensus spatial, puis confirmation temporelle.
/// </summary>
internal sealed class SuperKeyframeRecognizer
{
    private static readonly (int AX, int AY, int BX, int BY)[] CensusPairs =
    [
        (-3, 0, 3, 0),
        (0, -3, 0, 3),
        (-3, -3, 3, 3),
        (3, -3, -3, 3),
        (-3, -1, 3, 1),
        (-3, 1, 3, -1),
        (-1, -3, 1, 3),
        (1, -3, -1, 3),
        (-2, -2, 2, 0),
        (2, 0, -2, 2),
        (-2, 0, 2, -2),
        (0, -2, 2, 2),
        (-3, 0, 0, -3),
        (0, -3, 3, 0),
        (3, 0, 0, 3),
        (0, 3, -3, 0),
    ];

    private static readonly (int X, int Y)[] AlignmentOffsets =
    [
        (0, 0),
        (-2, 0),
        (2, 0),
        (0, -2),
        (0, 2),
    ];

    private readonly object _sync = new();
    private readonly SuperKeyframeRecognizerSettings _settings;
    private readonly Dictionary<string, MapKeyframes> _maps =
        new(StringComparer.Ordinal);

    private long _nextKeyframeId = 1;
    private long _touchSequence;
    private long _pendingKeyframeId;
    private int _pendingMatches;
    private bool _pendingGlobalLoopClosure;
    private long _pendingLastMatchTimestamp;
    private long _cooldownUntilTimestamp;
    private long _lastObservationTimestamp;

    internal SuperKeyframeRecognizer(
        SuperKeyframeRecognizerSettings? settings = null)
    {
        _settings = settings ?? new SuperKeyframeRecognizerSettings();
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

    internal int KeyframeCountForMap(string mapId)
    {
        lock (_sync)
        {
            var normalized = NormalizeMapId(mapId);
            return _maps.TryGetValue(normalized, out var map)
                ? map.Keyframes.Count
                : 0;
        }
    }

    internal void ClearAllKeyframes()
    {
        lock (_sync)
        {
            _maps.Clear();
            _nextKeyframeId = 1;
            _touchSequence = 0;
            ResetSessionCore();
        }
    }

    internal void ResetSession()
    {
        lock (_sync)
        {
            ResetSessionCore();
        }
    }

    internal void CancelPendingMatch()
    {
        lock (_sync)
        {
            ClearPending();
        }
    }

    internal bool AddExactKeyframe(
        string mapId,
        ReadOnlySpan<byte> luminance,
        VisualKeyframePose exactPose,
        long timestamp,
        out long keyframeId)
    {
        lock (_sync)
        {
            var normalizedMapId = NormalizeMapId(mapId);
            ValidateFrame(luminance);
            ValidatePose(exactPose);
            if (timestamp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            var descriptor = CreateDescriptor(luminance, 0, 0);
            if (!descriptor.IsUsable(_settings))
            {
                keyframeId = 0;
                return false;
            }

            var map = GetOrCreateMap(normalizedMapId);
            map.LastTouchedSequence = ++_touchSequence;

            for (var index = 0; index < map.Keyframes.Count; index++)
            {
                var existing = map.Keyframes[index];
                if (SpatialDistance(existing.Pose, exactPose) <= 2.0
                    && AngleDistance(
                        existing.Pose.YawDegrees,
                        exactPose.YawDegrees) <= 7.0
                    && TryCompare(
                        existing.Descriptor,
                        descriptor,
                        out var comparison)
                    && comparison.CompositeDistance <= 0.13)
                {
                    map.Keyframes[index] = existing with
                    {
                        Pose = exactPose with
                        {
                            YawDegrees = Normalize360(exactPose.YawDegrees),
                        },
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
                        oldestTimestamp =
                            map.Keyframes[index].CapturedAtTimestamp;
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
                exactPose with
                {
                    YawDegrees = Normalize360(exactPose.YawDegrees),
                },
                descriptor,
                timestamp));
            return true;
        }
    }

    internal SuperKeyframeRecognition Observe(
        string mapId,
        ReadOnlySpan<byte> luminance,
        VisualKeyframePose estimatedPose,
        long timestamp,
        double estimatedPositionUncertaintyMeters)
    {
        lock (_sync)
        {
            var normalizedMapId = NormalizeMapId(mapId);
            ValidateFrame(luminance);
            ValidatePose(estimatedPose);
            if (timestamp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }
            if (!double.IsFinite(estimatedPositionUncertaintyMeters)
                || estimatedPositionUncertaintyMeters < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(estimatedPositionUncertaintyMeters));
            }

            timestamp = Math.Max(timestamp, _lastObservationTimestamp);
            _lastObservationTimestamp = timestamp;

            if (timestamp < _cooldownUntilTimestamp)
            {
                ClearPending();
                return SuperKeyframeRecognition.Cooldown();
            }

            if (!_maps.TryGetValue(normalizedMapId, out var map)
                || map.Keyframes.Count == 0)
            {
                ClearPending();
                return SuperKeyframeRecognition.NoMatch();
            }

            map.LastTouchedSequence = ++_touchSequence;
            var queries = new List<FrameDescriptor>(AlignmentOffsets.Length);
            foreach (var offset in AlignmentOffsets)
            {
                var descriptor = CreateDescriptor(
                    luminance,
                    offset.X,
                    offset.Y);
                if (descriptor.IsUsable(_settings))
                {
                    queries.Add(descriptor);
                }
            }
            if (queries.Count == 0)
            {
                ClearPending();
                return SuperKeyframeRecognition.NoMatch();
            }

            MatchCandidate? best = null;
            MatchCandidate? second = null;
            foreach (var keyframe in map.Keyframes)
            {
                MatchCandidate? keyframeBest = null;
                foreach (var query in queries)
                {
                    if (!TryCompare(
                            keyframe.Descriptor,
                            query,
                            out var comparison))
                    {
                        continue;
                    }

                    var candidate = new MatchCandidate(
                        keyframe,
                        comparison.CompositeDistance,
                        comparison.RegionalConsensus,
                        comparison.ComparedBits);
                    if (keyframeBest is null
                        || candidate.Distance < keyframeBest.Value.Distance)
                    {
                        keyframeBest = candidate;
                    }
                }

                if (keyframeBest is not { } candidateBest)
                {
                    continue;
                }

                if (best is null
                    || candidateBest.Distance < best.Value.Distance)
                {
                    second = best;
                    best = candidateBest;
                }
                else if (second is null
                    || candidateBest.Distance < second.Value.Distance)
                {
                    second = candidateBest;
                }
            }

            if (best is not { } winner
                || winner.Distance > _settings.MaximumCompositeDistance
                || winner.RegionalConsensus < _settings.MinimumRegionalConsensus)
            {
                ClearPending();
                return SuperKeyframeRecognition.NoMatch();
            }

            var uniquenessMargin = second is { } runnerUp
                ? runnerUp.Distance - winner.Distance
                : 1.0 - winner.Distance;
            if (uniquenessMargin < _settings.MinimumBestSecondMargin)
            {
                ClearPending();
                return SuperKeyframeRecognition.NoMatch();
            }

            var spatialDistance = SpatialDistance(
                winner.Keyframe.Pose,
                estimatedPose);
            var verticalDistance = Math.Abs(
                winner.Keyframe.Pose.Y - estimatedPose.Y);
            var yawDistance = AngleDistance(
                winner.Keyframe.Pose.YawDegrees,
                estimatedPose.YawDegrees);

            var localPoseCompatible =
                spatialDistance <= _settings.MaximumEstimatedDistanceMeters
                && verticalDistance <= _settings.MaximumVerticalDistanceMeters
                && yawDistance <= _settings.MaximumEstimatedYawDifferenceDegrees;

            var globalLoopClosure = false;
            if (!localPoseCompatible)
            {
                var strongGlobalEvidence =
                    winner.Distance <= _settings.GlobalMaximumCompositeDistance
                    && uniquenessMargin >= _settings.GlobalMinimumBestSecondMargin
                    && winner.RegionalConsensus
                        >= _settings.GlobalMinimumRegionalConsensus;
                var globallyPlausible =
                    verticalDistance
                        <= _settings.GlobalMaximumVerticalDistanceMeters
                    && yawDistance
                        <= _settings.GlobalMaximumEstimatedYawDifferenceDegrees;
                if (estimatedPositionUncertaintyMeters
                        < _settings.GlobalLoopClosureMinimumUncertaintyMeters
                    || !strongGlobalEvidence
                    || !globallyPlausible)
                {
                    ClearPending();
                    return SuperKeyframeRecognition.NoMatch();
                }

                globalLoopClosure = true;
            }

            if (_pendingKeyframeId == winner.Keyframe.Id
                && _pendingGlobalLoopClosure == globalLoopClosure)
            {
                var elapsed = timestamp - _pendingLastMatchTimestamp;
                var minimumTicks = SecondsToTimestampTicks(
                    _settings.MinimumConfirmationInterval.TotalSeconds);
                var maximumTicks = SecondsToTimestampTicks(
                    _settings.MaximumConfirmationGap.TotalSeconds);
                if (elapsed < minimumTicks)
                {
                    return Candidate(
                        winner,
                        uniquenessMargin,
                        _pendingMatches,
                        globalLoopClosure);
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
                _pendingGlobalLoopClosure = globalLoopClosure;
                _pendingLastMatchTimestamp = timestamp;
            }

            var requiredMatches = globalLoopClosure
                ? _settings.GlobalRequiredConsecutiveMatches
                : _settings.RequiredConsecutiveMatches;
            if (_pendingMatches < requiredMatches)
            {
                return Candidate(
                    winner,
                    uniquenessMargin,
                    _pendingMatches,
                    globalLoopClosure);
            }

            var confirmedCount = _pendingMatches;
            var cooldownTicks = SecondsToTimestampTicks(
                _settings.ConfirmationCooldown.TotalSeconds);
            _cooldownUntilTimestamp = SaturatingAdd(
                timestamp,
                cooldownTicks);
            ClearPending();

            return new SuperKeyframeRecognition(
                SuperKeyframeRecognitionState.Confirmed,
                winner.Keyframe.Id,
                winner.Keyframe.Pose,
                confirmedCount,
                winner.Distance,
                uniquenessMargin,
                winner.RegionalConsensus,
                winner.ComparedBits,
                globalLoopClosure);
        }
    }

    internal SuperKeyframeDatabaseSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            var snapshot = new SuperKeyframeDatabaseSnapshot
            {
                FrameWidth = _settings.FrameWidth,
                FrameHeight = _settings.FrameHeight,
            };

            foreach (var pair in _maps
                .OrderBy(pair => pair.Value.LastTouchedSequence)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var mapSnapshot = new SuperKeyframeMapSnapshot
                {
                    MapId = pair.Key,
                };
                foreach (var keyframe in pair.Value.Keyframes
                    .OrderBy(keyframe => keyframe.CapturedAtTimestamp)
                    .ThenBy(keyframe => keyframe.Id))
                {
                    mapSnapshot.Keyframes.Add(new SuperKeyframeEntrySnapshot
                    {
                        X = keyframe.Pose.X,
                        Y = keyframe.Pose.Y,
                        Z = keyframe.Pose.Z,
                        YawDegrees = keyframe.Pose.YawDegrees,
                        GlobalBase64 = Convert.ToBase64String(
                            keyframe.Descriptor.Global),
                        GlobalValidBase64 = Convert.ToBase64String(
                            keyframe.Descriptor.GlobalValid),
                        GradientBase64 = Convert.ToBase64String(
                            keyframe.Descriptor.Gradient),
                        CensusBitsBase64 = EncodeWords(
                            keyframe.Descriptor.CensusBits),
                        CensusValidBase64 = EncodeWords(
                            keyframe.Descriptor.CensusValid),
                        RegionHashesBase64 = EncodeWords(
                            keyframe.Descriptor.RegionHashes),
                        RegionValidBase64 = EncodeWords(
                            keyframe.Descriptor.RegionValid),
                        ValidBitCount = keyframe.Descriptor.ValidBitCount,
                        TextureStandardDeviation =
                            keyframe.Descriptor.TextureStandardDeviation,
                        GradientEnergy = keyframe.Descriptor.GradientEnergy,
                        MeanLuminance = keyframe.Descriptor.MeanLuminance,
                    });
                }
                snapshot.Maps.Add(mapSnapshot);
            }

            return snapshot;
        }
    }

    internal bool TryImportSnapshot(SuperKeyframeDatabaseSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return false;
        }

        lock (_sync)
        {
            try
            {
                if (snapshot.SchemaVersion != 1
                    || snapshot.FrameWidth != _settings.FrameWidth
                    || snapshot.FrameHeight != _settings.FrameHeight
                    || snapshot.Maps is null
                    || snapshot.Maps.Count > _settings.MaximumMaps)
                {
                    return false;
                }

                var globalLength =
                    _settings.GlobalColumns * _settings.GlobalRows;
                var gradientLength =
                    _settings.GradientColumns
                    * _settings.GradientRows
                    * _settings.GradientBins;
                var censusBitCapacity =
                    _settings.CensusColumns
                    * _settings.CensusRows
                    * CensusPairs.Length;
                var censusWords = (censusBitCapacity + 63) / 64;
                var regionCount =
                    _settings.RegionColumns * _settings.RegionRows;

                var imported =
                    new Dictionary<string, MapKeyframes>(StringComparer.Ordinal);
                var nextId = 1L;
                var touchSequence = 0L;
                var captureOrder = 0L;

                foreach (var mapSnapshot in snapshot.Maps)
                {
                    if (mapSnapshot is null
                        || mapSnapshot.Keyframes is null
                        || mapSnapshot.Keyframes.Count
                            > _settings.MaximumKeyframesPerMap)
                    {
                        return false;
                    }

                    var mapId = NormalizeMapId(mapSnapshot.MapId);
                    if (imported.ContainsKey(mapId))
                    {
                        return false;
                    }

                    var map = new MapKeyframes
                    {
                        LastTouchedSequence = ++touchSequence,
                    };

                    foreach (var entry in mapSnapshot.Keyframes)
                    {
                        if (entry is null
                            || !TryDecodeBytes(
                                entry.GlobalBase64,
                                globalLength,
                                out var global)
                            || !TryDecodeBytes(
                                entry.GlobalValidBase64,
                                globalLength,
                                out var globalValid)
                            || !TryDecodeBytes(
                                entry.GradientBase64,
                                gradientLength,
                                out var gradient)
                            || !TryDecodeWords(
                                entry.CensusBitsBase64,
                                censusWords,
                                out var censusBits)
                            || !TryDecodeWords(
                                entry.CensusValidBase64,
                                censusWords,
                                out var censusValid)
                            || !TryDecodeWords(
                                entry.RegionHashesBase64,
                                regionCount,
                                out var regionHashes)
                            || !TryDecodeWords(
                                entry.RegionValidBase64,
                                regionCount,
                                out var regionValid))
                        {
                            return false;
                        }

                        var pose = new VisualKeyframePose(
                            entry.X,
                            entry.Y,
                            entry.Z,
                            Normalize360(entry.YawDegrees));
                        ValidatePose(pose);

                        if (!double.IsFinite(entry.TextureStandardDeviation)
                            || !double.IsFinite(entry.GradientEnergy)
                            || !double.IsFinite(entry.MeanLuminance)
                            || entry.TextureStandardDeviation < 0
                            || entry.GradientEnergy < 0
                            || entry.MeanLuminance is < 0 or > 255
                            || entry.ValidBitCount != CountBits(censusValid)
                            || entry.ValidBitCount < 0
                            || !DescriptorBitsAreValid(
                                censusBits,
                                censusValid,
                                censusBitCapacity))
                        {
                            return false;
                        }

                        var descriptor = new FrameDescriptor(
                            global,
                            globalValid,
                            gradient,
                            censusBits,
                            censusValid,
                            regionHashes,
                            regionValid,
                            entry.ValidBitCount,
                            entry.TextureStandardDeviation,
                            entry.GradientEnergy,
                            entry.MeanLuminance);
                        if (!descriptor.IsUsable(_settings))
                        {
                            return false;
                        }

                        map.Keyframes.Add(new Keyframe(
                            nextId++,
                            pose,
                            descriptor,
                            ++captureOrder));
                    }

                    imported.Add(mapId, map);
                }

                _maps.Clear();
                foreach (var pair in imported)
                {
                    _maps.Add(pair.Key, pair.Value);
                }
                _nextKeyframeId = nextId;
                _touchSequence = touchSequence;
                ResetSessionCore();
                return true;
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                ArgumentOutOfRangeException or
                FormatException or
                OverflowException)
            {
                return false;
            }
        }
    }

    private FrameDescriptor CreateDescriptor(
        ReadOnlySpan<byte> luminance,
        int shiftX,
        int shiftY)
    {
        var global = CreateGlobalSignature(
            luminance,
            shiftX,
            shiftY,
            out var globalValid,
            out var meanLuminance,
            out var textureStandardDeviation);
        var gradient = CreateGradientSignature(
            luminance,
            shiftX,
            shiftY,
            out var gradientEnergy);
        var (censusBits, censusValid, validBitCount) =
            CreateCensusSignature(luminance, shiftX, shiftY);
        var (regionHashes, regionValid) =
            CreateRegionalHashes(luminance, shiftX, shiftY);

        return new FrameDescriptor(
            global,
            globalValid,
            gradient,
            censusBits,
            censusValid,
            regionHashes,
            regionValid,
            validBitCount,
            textureStandardDeviation,
            gradientEnergy,
            meanLuminance);
    }

    private byte[] CreateGlobalSignature(
        ReadOnlySpan<byte> luminance,
        int shiftX,
        int shiftY,
        out byte[] valid,
        out double meanLuminance,
        out double textureStandardDeviation)
    {
        var length = _settings.GlobalColumns * _settings.GlobalRows;
        var means = new double[length];
        valid = new byte[length];
        var allSamples = new List<byte>(
            _settings.FrameWidth * _settings.FrameHeight / 6);

        for (var gy = 0; gy < _settings.GlobalRows; gy++)
        {
            var y0 = gy * _settings.FrameHeight / _settings.GlobalRows;
            var y1 = (gy + 1) * _settings.FrameHeight / _settings.GlobalRows;
            for (var gx = 0; gx < _settings.GlobalColumns; gx++)
            {
                var x0 = gx * _settings.FrameWidth / _settings.GlobalColumns;
                var x1 = (gx + 1) * _settings.FrameWidth / _settings.GlobalColumns;
                long sum = 0;
                var count = 0;

                for (var y = y0 + 1; y < y1 - 1; y += 2)
                {
                    for (var x = x0 + 1; x < x1 - 1; x += 2)
                    {
                        var sx = x + shiftX;
                        var sy = y + shiftY;
                        if (!IsSceneSample(sx, sy))
                        {
                            continue;
                        }

                        var value = luminance[
                            (sy * _settings.FrameWidth) + sx];
                        sum += value;
                        count++;
                        allSamples.Add(value);
                    }
                }

                var index = (gy * _settings.GlobalColumns) + gx;
                if (count >= 4)
                {
                    means[index] = sum / (double)count;
                    valid[index] = 1;
                }
            }
        }

        if (allSamples.Count == 0)
        {
            meanLuminance = 0;
            textureStandardDeviation = 0;
            return new byte[length];
        }

        meanLuminance = allSamples.Average(value => (double)value);
        var meanForVariance = meanLuminance;
        var variance = allSamples.Sum(value =>
        {
            var delta = value - meanForVariance;
            return delta * delta;
        }) / allSamples.Count;
        textureStandardDeviation = Math.Sqrt(Math.Max(0, variance));

        var normalized = new byte[length];
        var scale = Math.Max(8.0, textureStandardDeviation);
        for (var index = 0; index < length; index++)
        {
            if (valid[index] == 0)
            {
                normalized[index] = 128;
                continue;
            }

            var z = (means[index] - meanLuminance) / scale;
            normalized[index] = (byte)Math.Clamp(
                (int)Math.Round(128 + (z * 32)),
                0,
                255);
        }

        return normalized;
    }

    private byte[] CreateGradientSignature(
        ReadOnlySpan<byte> luminance,
        int shiftX,
        int shiftY,
        out double gradientEnergy)
    {
        var cellCount =
            _settings.GradientColumns * _settings.GradientRows;
        var bins = new double[cellCount * _settings.GradientBins];
        var totalEnergy = 0.0;
        var energySamples = 0;

        for (var y = 2; y < _settings.FrameHeight - 2; y += 2)
        {
            for (var x = 2; x < _settings.FrameWidth - 2; x += 2)
            {
                var sx = x + shiftX;
                var sy = y + shiftY;
                if (!IsSceneSample(sx, sy)
                    || sx <= 0
                    || sy <= 0
                    || sx >= _settings.FrameWidth - 1
                    || sy >= _settings.FrameHeight - 1)
                {
                    continue;
                }

                var dx =
                    luminance[(sy * _settings.FrameWidth) + sx + 1]
                    - luminance[(sy * _settings.FrameWidth) + sx - 1];
                var dy =
                    luminance[((sy + 1) * _settings.FrameWidth) + sx]
                    - luminance[((sy - 1) * _settings.FrameWidth) + sx];
                var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
                if (magnitude < 2.0)
                {
                    continue;
                }

                var angle = Math.Atan2(dy, dx);
                if (angle < 0)
                {
                    angle += Math.PI;
                }
                if (angle >= Math.PI)
                {
                    angle -= Math.PI;
                }

                var bin = Math.Min(
                    _settings.GradientBins - 1,
                    (int)(angle / Math.PI * _settings.GradientBins));
                var cellX = Math.Min(
                    _settings.GradientColumns - 1,
                    x * _settings.GradientColumns / _settings.FrameWidth);
                var cellY = Math.Min(
                    _settings.GradientRows - 1,
                    y * _settings.GradientRows / _settings.FrameHeight);
                var offset =
                    ((cellY * _settings.GradientColumns) + cellX)
                    * _settings.GradientBins;
                bins[offset + bin] += magnitude;
                totalEnergy += magnitude;
                energySamples++;
            }
        }

        gradientEnergy = energySamples == 0
            ? 0
            : totalEnergy / energySamples;

        var result = new byte[bins.Length];
        for (var cell = 0; cell < cellCount; cell++)
        {
            var offset = cell * _settings.GradientBins;
            var sum = 0.0;
            for (var bin = 0; bin < _settings.GradientBins; bin++)
            {
                sum += bins[offset + bin];
            }

            if (sum <= 0)
            {
                continue;
            }

            for (var bin = 0; bin < _settings.GradientBins; bin++)
            {
                result[offset + bin] = (byte)Math.Clamp(
                    (int)Math.Round(bins[offset + bin] / sum * 255.0),
                    0,
                    255);
            }
        }

        return result;
    }

    private (ulong[] Bits, ulong[] Valid, int Count) CreateCensusSignature(
        ReadOnlySpan<byte> luminance,
        int shiftX,
        int shiftY)
    {
        var bitCapacity =
            _settings.CensusColumns
            * _settings.CensusRows
            * CensusPairs.Length;
        var words = (bitCapacity + 63) / 64;
        var bits = new ulong[words];
        var valid = new ulong[words];
        var bitIndex = 0;
        var validCount = 0;

        var margin = _settings.CensusRadius + 3;
        var usableWidth = _settings.FrameWidth - (margin * 2);
        var usableHeight = _settings.FrameHeight - (margin * 2);

        for (var gy = 0; gy < _settings.CensusRows; gy++)
        {
            var y = SampleCoordinate(
                margin,
                usableHeight,
                gy,
                _settings.CensusRows) + shiftY;
            for (var gx = 0; gx < _settings.CensusColumns; gx++)
            {
                var x = SampleCoordinate(
                    margin,
                    usableWidth,
                    gx,
                    _settings.CensusColumns) + shiftX;
                var scene = IsSceneSample(x, y);

                foreach (var pair in CensusPairs)
                {
                    if (scene
                        && IsInside(
                            x + pair.AX,
                            y + pair.AY)
                        && IsInside(
                            x + pair.BX,
                            y + pair.BY))
                    {
                        var a = luminance[
                            ((y + pair.AY) * _settings.FrameWidth)
                            + x + pair.AX];
                        var b = luminance[
                            ((y + pair.BY) * _settings.FrameWidth)
                            + x + pair.BX];
                        if (Math.Abs(a - b)
                            >= _settings.MinimumCensusContrast)
                        {
                            SetBit(valid, bitIndex);
                            validCount++;
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

        return (bits, valid, validCount);
    }

    private (ulong[] Hashes, ulong[] Valid) CreateRegionalHashes(
        ReadOnlySpan<byte> luminance,
        int shiftX,
        int shiftY)
    {
        var count = _settings.RegionColumns * _settings.RegionRows;
        var hashes = new ulong[count];
        var valid = new ulong[count];

        for (var ry = 0; ry < _settings.RegionRows; ry++)
        {
            var y0 = ry * _settings.FrameHeight / _settings.RegionRows;
            var y1 = (ry + 1) * _settings.FrameHeight / _settings.RegionRows;
            for (var rx = 0; rx < _settings.RegionColumns; rx++)
            {
                var x0 = rx * _settings.FrameWidth / _settings.RegionColumns;
                var x1 = (rx + 1) * _settings.FrameWidth / _settings.RegionColumns;
                ulong hash = 0;
                ulong mask = 0;
                var bit = 0;

                for (var syIndex = 0; syIndex < 8; syIndex++)
                {
                    var y = y0
                        + ((syIndex * 2 + 1) * Math.Max(1, y1 - y0) / 16)
                        + shiftY;
                    for (var sxIndex = 0; sxIndex < 8; sxIndex++)
                    {
                        var xa = x0
                            + ((sxIndex * 2 + 1)
                                * Math.Max(1, x1 - x0) / 18)
                            + shiftX;
                        var xb = x0
                            + (((sxIndex + 1) * 2 + 1)
                                * Math.Max(1, x1 - x0) / 18)
                            + shiftX;

                        if (IsSceneSample(xa, y)
                            && IsSceneSample(xb, y)
                            && IsInside(xa, y)
                            && IsInside(xb, y))
                        {
                            var a = luminance[
                                (y * _settings.FrameWidth) + xa];
                            var b = luminance[
                                (y * _settings.FrameWidth) + xb];
                            if (Math.Abs(a - b)
                                >= _settings.MinimumCensusContrast)
                            {
                                mask |= 1UL << bit;
                                if (a > b)
                                {
                                    hash |= 1UL << bit;
                                }
                            }
                        }

                        bit++;
                    }
                }

                var index = (ry * _settings.RegionColumns) + rx;
                hashes[index] = hash;
                valid[index] = mask;
            }
        }

        return (hashes, valid);
    }

    private bool TryCompare(
        FrameDescriptor reference,
        FrameDescriptor query,
        out DescriptorComparison comparison)
    {
        var comparedBits = 0;
        var differentBits = 0;
        for (var index = 0;
             index < reference.CensusBits.Length;
             index++)
        {
            var common =
                reference.CensusValid[index]
                & query.CensusValid[index];
            comparedBits += BitOperations.PopCount(common);
            differentBits += BitOperations.PopCount(
                (reference.CensusBits[index]
                    ^ query.CensusBits[index])
                & common);
        }
        if (comparedBits < _settings.MinimumComparedBits)
        {
            comparison = default;
            return false;
        }

        var censusDistance = differentBits / (double)comparedBits;

        var globalDifference = 0.0;
        var globalCompared = 0;
        for (var index = 0;
             index < reference.Global.Length;
             index++)
        {
            if (reference.GlobalValid[index] == 0
                || query.GlobalValid[index] == 0)
            {
                continue;
            }

            globalDifference += Math.Abs(
                reference.Global[index] - query.Global[index]);
            globalCompared++;
        }
        if (globalCompared
            < reference.Global.Length * 0.55)
        {
            comparison = default;
            return false;
        }
        var globalDistance =
            globalDifference / (globalCompared * 255.0);

        var gradientDifference = 0.0;
        var gradientComparedCells = 0;
        for (var cell = 0;
             cell < _settings.GradientColumns * _settings.GradientRows;
             cell++)
        {
            var offset = cell * _settings.GradientBins;
            var referenceSum = 0;
            var querySum = 0;
            for (var bin = 0; bin < _settings.GradientBins; bin++)
            {
                referenceSum += reference.Gradient[offset + bin];
                querySum += query.Gradient[offset + bin];
            }
            if (referenceSum == 0 || querySum == 0)
            {
                continue;
            }

            for (var bin = 0; bin < _settings.GradientBins; bin++)
            {
                gradientDifference += Math.Abs(
                    reference.Gradient[offset + bin]
                    - query.Gradient[offset + bin]);
            }
            gradientComparedCells++;
        }
        if (gradientComparedCells
            < _settings.GradientColumns * _settings.GradientRows * 0.45)
        {
            comparison = default;
            return false;
        }
        var gradientDistance =
            gradientDifference
            / (gradientComparedCells
                * _settings.GradientBins
                * 255.0);

        var comparableRegions = 0;
        var agreeingRegions = 0;
        for (var index = 0;
             index < reference.RegionHashes.Length;
             index++)
        {
            var common =
                reference.RegionValid[index]
                & query.RegionValid[index];
            var commonCount = BitOperations.PopCount(common);
            if (commonCount < _settings.MinimumRegionComparedBits)
            {
                continue;
            }

            comparableRegions++;
            var different = BitOperations.PopCount(
                (reference.RegionHashes[index]
                    ^ query.RegionHashes[index])
                & common);
            var distance = different / (double)commonCount;
            if (distance <= _settings.MaximumRegionHammingDistance)
            {
                agreeingRegions++;
            }
        }
        var regionalConsensus = comparableRegions == 0
            ? 0
            : agreeingRegions / (double)comparableRegions;

        var composite =
            (censusDistance * 0.50)
            + (globalDistance * 0.20)
            + (gradientDistance * 0.18)
            + ((1.0 - regionalConsensus) * 0.12);

        comparison = new DescriptorComparison(
            composite,
            regionalConsensus,
            comparedBits);
        return true;
    }

    private bool IsSceneSample(int x, int y)
    {
        if (!IsInside(x, y))
        {
            return false;
        }

        var width = _settings.FrameWidth;
        var height = _settings.FrameHeight;
        if (x < width * _settings.SideHudMaskFraction
            || x >= width * (1.0 - _settings.SideHudMaskFraction)
            || y < height * _settings.TopHudMaskFraction
            || y >= height * (1.0 - _settings.BottomHudMaskFraction))
        {
            return false;
        }

        if (y >= height * _settings.WeaponMaskTopFraction
            && x >= width * _settings.WeaponMaskLeftFraction
            && x <= width * _settings.WeaponMaskRightFraction)
        {
            return false;
        }

        return true;
    }

    private bool IsInside(int x, int y) =>
        x >= 0
        && y >= 0
        && x < _settings.FrameWidth
        && y < _settings.FrameHeight;

    private MapKeyframes GetOrCreateMap(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var existing))
        {
            return existing;
        }

        if (_maps.Count >= _settings.MaximumMaps)
        {
            var oldest = _maps
                .OrderBy(pair => pair.Value.LastTouchedSequence)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First();
            _maps.Remove(oldest.Key);
        }

        var created = new MapKeyframes();
        _maps.Add(mapId, created);
        return created;
    }

    private static SuperKeyframeRecognition Candidate(
        MatchCandidate candidate,
        double uniquenessMargin,
        int count,
        bool globalLoopClosure) => new(
            SuperKeyframeRecognitionState.Candidate,
            candidate.Keyframe.Id,
            null,
            count,
            candidate.Distance,
            uniquenessMargin,
            candidate.RegionalConsensus,
            candidate.ComparedBits,
            globalLoopClosure);

    private void ResetSessionCore()
    {
        ClearPending();
        _cooldownUntilTimestamp = 0;
        _lastObservationTimestamp = 0;
    }

    private void ClearPending()
    {
        _pendingKeyframeId = 0;
        _pendingMatches = 0;
        _pendingGlobalLoopClosure = false;
        _pendingLastMatchTimestamp = 0;
    }

    private void ValidateFrame(ReadOnlySpan<byte> luminance)
    {
        if (luminance.Length
            != _settings.FrameWidth * _settings.FrameHeight)
        {
            throw new ArgumentException(
                "La trame SuperKeyframe a une taille invalide.",
                nameof(luminance));
        }
    }

    private static void ValidatePose(VisualKeyframePose pose)
    {
        if (!new[] { pose.X, pose.Y, pose.Z, pose.YawDegrees }.All(double.IsFinite)
            || Math.Abs(pose.X) > 1_000_000
            || Math.Abs(pose.Y) > 1_000_000
            || Math.Abs(pose.Z) > 1_000_000
            || Math.Abs(pose.YawDegrees) > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(pose));
        }
    }

    private static void ValidateSettings(
        SuperKeyframeRecognizerSettings settings)
    {
        if (settings.FrameWidth != HighResolutionVisualSampler.SampleWidth
            || settings.FrameHeight != HighResolutionVisualSampler.SampleHeight
            || settings.GlobalColumns < 2
            || settings.GlobalRows < 2
            || settings.CensusColumns < 2
            || settings.CensusRows < 2
            || settings.CensusRadius < 1
            || settings.MinimumCensusContrast < 1
            || settings.MinimumComparedBits < 1
            || settings.GradientColumns < 2
            || settings.GradientRows < 2
            || settings.GradientBins is < 4 or > 16
            || settings.RegionColumns < 2
            || settings.RegionRows < 2
            || settings.MinimumRegionComparedBits is < 1 or > 64
            || !double.IsFinite(settings.MaximumRegionHammingDistance)
            || settings.MaximumRegionHammingDistance is <= 0 or >= 1
            || !double.IsFinite(settings.MinimumTextureStandardDeviation)
            || settings.MinimumTextureStandardDeviation < 0
            || !double.IsFinite(settings.MinimumGradientEnergy)
            || settings.MinimumGradientEnergy < 0
            || !double.IsFinite(settings.MaximumCompositeDistance)
            || settings.MaximumCompositeDistance is <= 0 or >= 1
            || !double.IsFinite(settings.MinimumBestSecondMargin)
            || settings.MinimumBestSecondMargin is < 0 or >= 1
            || !double.IsFinite(settings.MinimumRegionalConsensus)
            || settings.MinimumRegionalConsensus is < 0 or > 1
            || settings.RequiredConsecutiveMatches < 2
            || settings.GlobalRequiredConsecutiveMatches
                < settings.RequiredConsecutiveMatches
            || settings.MaximumKeyframesPerMap < 1
            || settings.MaximumMaps < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
    }

    private static int SampleCoordinate(
        int margin,
        int usable,
        int index,
        int count) =>
        margin + ((index * 2 + 1) * usable / (count * 2));

    private static void SetBit(ulong[] words, int bitIndex)
    {
        words[bitIndex / 64] |= 1UL << (bitIndex % 64);
    }

    private static double SpatialDistance(
        VisualKeyframePose a,
        VisualKeyframePose b) =>
        Math.Sqrt(
            ((a.X - b.X) * (a.X - b.X))
            + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static double AngleDistance(double a, double b) =>
        Math.Abs(NormalizeSigned180(a - b));

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

    private static double Normalize360(double angle)
    {
        angle %= 360.0;
        return angle < 0 ? angle + 360.0 : angle;
    }

    private static string NormalizeMapId(string mapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        var normalized = mapId.Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 64
            || normalized.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')))
        {
            throw new ArgumentException(
                "Identifiant de carte invalide.",
                nameof(mapId));
        }
        return normalized;
    }

    private static long SecondsToTimestampTicks(double seconds)
    {
        var ticks = seconds * Stopwatch.Frequency;
        return ticks >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Ceiling(Math.Max(0, ticks));
    }

    private static long SaturatingAdd(long value, long delta)
    {
        if (delta <= 0)
        {
            return value;
        }
        return value > long.MaxValue - delta
            ? long.MaxValue
            : value + delta;
    }

    private static string EncodeWords(ulong[] words)
    {
        var bytes = new byte[words.Length * sizeof(ulong)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(index * sizeof(ulong), sizeof(ulong)),
                words[index]);
        }
        return Convert.ToBase64String(bytes);
    }

    private static bool TryDecodeWords(
        string? encoded,
        int expectedCount,
        out ulong[] words)
    {
        words = Array.Empty<ulong>();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != expectedCount * sizeof(ulong))
        {
            return false;
        }

        words = new ulong[expectedCount];
        for (var index = 0; index < expectedCount; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt64LittleEndian(
                bytes.AsSpan(index * sizeof(ulong), sizeof(ulong)));
        }
        return true;
    }

    private static bool TryDecodeBytes(
        string? encoded,
        int expectedLength,
        out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        return bytes.Length == expectedLength;
    }

    private static bool DescriptorBitsAreValid(
        ulong[] bits,
        ulong[] valid,
        int bitCapacity)
    {
        if (bits.Length == 0 || bits.Length != valid.Length)
        {
            return false;
        }

        for (var index = 0; index < bits.Length; index++)
        {
            if ((bits[index] & ~valid[index]) != 0)
            {
                return false;
            }
        }

        var remainder = bitCapacity % 64;
        if (remainder != 0)
        {
            var mask = (1UL << remainder) - 1UL;
            if ((bits[^1] & ~mask) != 0
                || (valid[^1] & ~mask) != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static int CountBits(ulong[] words) =>
        words.Sum(word => BitOperations.PopCount(word));

    private readonly record struct DescriptorComparison(
        double CompositeDistance,
        double RegionalConsensus,
        int ComparedBits);

    private sealed record FrameDescriptor(
        byte[] Global,
        byte[] GlobalValid,
        byte[] Gradient,
        ulong[] CensusBits,
        ulong[] CensusValid,
        ulong[] RegionHashes,
        ulong[] RegionValid,
        int ValidBitCount,
        double TextureStandardDeviation,
        double GradientEnergy,
        double MeanLuminance)
    {
        internal bool IsUsable(SuperKeyframeRecognizerSettings settings) =>
            ValidBitCount >= settings.MinimumComparedBits
            && TextureStandardDeviation
                >= settings.MinimumTextureStandardDeviation
            && GradientEnergy >= settings.MinimumGradientEnergy;
    }

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

    private readonly record struct MatchCandidate(
        Keyframe Keyframe,
        double Distance,
        double RegionalConsensus,
        int ComparedBits);
}