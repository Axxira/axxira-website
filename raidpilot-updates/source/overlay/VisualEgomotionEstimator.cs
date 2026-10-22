using System.Numerics;

namespace RaidPilot.Overlay;

internal enum VisualEgomotionState
{
    Priming,
    Unreliable,
    Stationary,
    Tracking,
}

internal enum VisualHorizontalDirection
{
    None,
    Left,
    Right,
}

/// <summary>
/// Résultat d'un déplacement d'image entre deux petites trames de luminance.
/// SceneShiftX/Y décrit le mouvement du décor à l'écran. La direction caméra
/// est l'opposé du déplacement horizontal du décor. EstimatedYawDeltaDegrees
/// n'est qu'une hypothèse de rotation pure : une translation latérale produit
/// un flux similaire et doit être départagée par les autres capteurs.
/// </summary>
internal readonly record struct VisualEgomotionEstimate(
    VisualEgomotionState State,
    double SceneShiftX,
    double SceneShiftY,
    VisualHorizontalDirection HorizontalCameraDirection,
    double EstimatedYawDeltaDegrees,
    double Confidence,
    int TexturedFeatureCount,
    int MatchedFeatureCount,
    int InlierFeatureCount,
    double InlierRatio,
    double ResidualPixels,
    double BrightnessDelta)
{
    internal bool IsReliable => State is VisualEgomotionState.Stationary or VisualEgomotionState.Tracking;

    internal static VisualEgomotionEstimate Priming(double brightnessDelta = 0) => new(
        VisualEgomotionState.Priming,
        0,
        0,
        VisualHorizontalDirection.None,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        brightnessDelta);

    internal static VisualEgomotionEstimate Unreliable(
        int texturedFeatureCount,
        int matchedFeatureCount,
        double brightnessDelta) => new(
        VisualEgomotionState.Unreliable,
        0,
        0,
        VisualHorizontalDirection.None,
        0,
        0,
        texturedFeatureCount,
        matchedFeatureCount,
        0,
        0,
        0,
        brightnessDelta);
}

internal sealed record VisualEgomotionSettings
{
    internal int PatchRadius { get; init; } = 3;

    internal int HorizontalSearchRadius { get; init; } = 6;

    internal int VerticalSearchRadius { get; init; } = 3;

    internal int GridColumns { get; init; } = 8;

    internal int GridRows { get; init; } = 5;

    internal int MinimumPatchRange { get; init; } = 18;

    internal int MaximumDescriptorDistance { get; init; } = 11;

    internal int MinimumUniquenessMargin { get; init; } = 3;

    internal int MaximumForwardBackwardError { get; init; } = 1;

    internal int MinimumTexturedFeatures { get; init; } = 8;

    internal int MinimumMatchedFeatures { get; init; } = 6;

    internal double MinimumInlierRatio { get; init; } = 0.58;

    internal double MinimumTexturedInlierRatio { get; init; } = 0.30;

    internal double InlierRadiusPixels { get; init; } = 1.25;

    internal int MinimumInlierColumnSpan { get; init; } = 3;

    internal int MinimumInlierRowSpan { get; init; } = 2;

    internal double MinimumReliableConfidence { get; init; } = 0.55;

    internal double MinimumHorizontalShiftPixels { get; init; } = 0.75;

    internal double SideHudMaskFraction { get; init; } = 0.07;

    internal double WeaponMaskTopFraction { get; init; } = 0.68;

    internal double WeaponMaskLeftFraction { get; init; } = 0.24;

    internal double WeaponMaskRightFraction { get; init; } = 0.76;

    internal double HorizontalFieldOfViewDegrees { get; init; } = 75;
}

/// <summary>
/// Odométrie visuelle à très faible coût destinée aux trames 64x36 du garde
/// visuel. Chaque point de grille reçoit une signature Census 7x7, robuste à
/// un changement additif de luminosité. Le meilleur déplacement est validé
/// par unicité et par un suivi inverse. Un consensus spatial de type RANSAC
/// rejette ensuite le HUD, l'arme, les objets mobiles et les animations.
/// </summary>
internal sealed class VisualEgomotionEstimator
{
    private const int DescriptorBitCapacity = 63;

    private readonly object _sync = new();
    private readonly VisualEgomotionSettings _settings;
    private double _horizontalFieldOfViewDegrees;

    private byte[] _previousFrame = [];
    private int _previousWidth;
    private int _previousHeight;
    private bool _hasPreviousFrame;

    internal VisualEgomotionEstimator(VisualEgomotionSettings? settings = null)
    {
        _settings = settings ?? new VisualEgomotionSettings();
        ValidateSettings(_settings);
        _horizontalFieldOfViewDegrees = _settings.HorizontalFieldOfViewDegrees;
    }

    internal double HorizontalFieldOfViewDegrees
    {
        get
        {
            lock (_sync)
            {
                return _horizontalFieldOfViewDegrees;
            }
        }
    }

    internal void SetHorizontalFieldOfViewDegrees(double degrees)
    {
        if (!double.IsFinite(degrees) || degrees is <= 1.0 or >= 179.0)
        {
            throw new ArgumentOutOfRangeException(nameof(degrees));
        }

        lock (_sync)
        {
            _horizontalFieldOfViewDegrees = degrees;
        }
    }

    internal VisualEgomotionEstimate Observe(
        ReadOnlySpan<byte> luminance,
        int width,
        int height,
        bool estimateMotion = true)
    {
        lock (_sync)
        {
            ValidateFrame(luminance, width, height);

            if (!_hasPreviousFrame || _previousWidth != width || _previousHeight != height)
            {
                SaveFrame(luminance, width, height);
                return VisualEgomotionEstimate.Priming();
            }

            // Feature matching is by far the most expensive part of the
            // visual guard. While keyboard or mouse input already proves that
            // the player is acting, keep the reference frame fresh without
            // doing a redundant descriptor search. The next quiet frame is
            // still compared against a recent image and can re-enable exact
            // keyframe recognition immediately.
            if (!estimateMotion)
            {
                SaveFrame(luminance, width, height);
                return VisualEgomotionEstimate.Priming();
            }

            var currentMean = CalculateMean(luminance);
            var brightnessDelta = currentMean - CalculateMean(_previousFrame);
            var result = Estimate(_previousFrame, luminance, width, height, brightnessDelta);
            SaveFrame(luminance, width, height);
            return result;
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _hasPreviousFrame = false;
            _previousWidth = 0;
            _previousHeight = 0;
            _previousFrame = [];
        }
    }

    private VisualEgomotionEstimate Estimate(
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        int width,
        int height,
        double brightnessDelta)
    {
        var horizontalMargin = _settings.PatchRadius + _settings.HorizontalSearchRadius;
        var verticalMargin = _settings.PatchRadius + _settings.VerticalSearchRadius;
        var usableWidth = width - (horizontalMargin * 2);
        var usableHeight = height - (verticalMargin * 2);
        if (usableWidth < _settings.GridColumns || usableHeight < _settings.GridRows)
        {
            return VisualEgomotionEstimate.Unreliable(0, 0, brightnessDelta);
        }

        var tracks = new List<FeatureTrack>(_settings.GridColumns * _settings.GridRows);
        var texturedFeatures = 0;

        for (var gridY = 0; gridY < _settings.GridRows; gridY++)
        {
            var y = SampleCoordinate(verticalMargin, usableHeight, gridY, _settings.GridRows);
            for (var gridX = 0; gridX < _settings.GridColumns; gridX++)
            {
                var x = SampleCoordinate(horizontalMargin, usableWidth, gridX, _settings.GridColumns);
                if (!IsScenePatch(x, y, width, height))
                {
                    continue;
                }

                if (!TryCreateDescriptor(previous, width, height, x, y, out var sourceDescriptor))
                {
                    continue;
                }

                texturedFeatures++;
                if (!TryFindUniqueMatch(
                        sourceDescriptor,
                        current,
                        width,
                        height,
                        x,
                        y,
                        _settings.HorizontalSearchRadius,
                        _settings.VerticalSearchRadius,
                        out var dx,
                        out var dy,
                        out var descriptorDistance,
                        out var uniquenessMargin))
                {
                    continue;
                }

                var matchedX = x + dx;
                var matchedY = y + dy;
                if (!IsScenePatch(matchedX, matchedY, width, height)
                    || !TryCreateDescriptor(current, width, height, matchedX, matchedY, out var matchedDescriptor)
                    || !TryFindUniqueMatch(
                        matchedDescriptor,
                        previous,
                        width,
                        height,
                        matchedX,
                        matchedY,
                        _settings.HorizontalSearchRadius,
                        _settings.VerticalSearchRadius,
                        out var reverseDx,
                        out var reverseDy,
                        out _,
                        out _)
                    || Math.Abs(dx + reverseDx) > _settings.MaximumForwardBackwardError
                    || Math.Abs(dy + reverseDy) > _settings.MaximumForwardBackwardError)
                {
                    continue;
                }

                var descriptorBits = DescriptorBitCount;
                var matchQuality = 1.0 - ((double)descriptorDistance / descriptorBits);
                var uniquenessQuality = Math.Clamp((double)uniquenessMargin / 10.0, 0, 1);
                tracks.Add(new FeatureTrack(
                    dx,
                    dy,
                    gridX,
                    gridY,
                    (matchQuality * 0.75) + (uniquenessQuality * 0.25)));
            }
        }

        if (texturedFeatures < _settings.MinimumTexturedFeatures
            || tracks.Count < _settings.MinimumMatchedFeatures)
        {
            return VisualEgomotionEstimate.Unreliable(texturedFeatures, tracks.Count, brightnessDelta);
        }

        var consensus = FindBestConsensus(tracks);
        if (consensus.Inliers.Count < _settings.MinimumMatchedFeatures)
        {
            return VisualEgomotionEstimate.Unreliable(texturedFeatures, tracks.Count, brightnessDelta);
        }

        var inlierRatio = (double)consensus.Inliers.Count / tracks.Count;
        var texturedInlierRatio = (double)consensus.Inliers.Count / texturedFeatures;
        var columnSpan = consensus.Inliers.Max(track => track.GridX)
            - consensus.Inliers.Min(track => track.GridX)
            + 1;
        var rowSpan = consensus.Inliers.Max(track => track.GridY)
            - consensus.Inliers.Min(track => track.GridY)
            + 1;
        if (inlierRatio < _settings.MinimumInlierRatio
            || texturedInlierRatio < _settings.MinimumTexturedInlierRatio
            || columnSpan < Math.Min(_settings.MinimumInlierColumnSpan, _settings.GridColumns)
            || rowSpan < Math.Min(_settings.MinimumInlierRowSpan, _settings.GridRows))
        {
            return VisualEgomotionEstimate.Unreliable(texturedFeatures, tracks.Count, brightnessDelta);
        }

        var shiftX = Median(consensus.Inliers.Select(track => (double)track.Dx));
        var shiftY = Median(consensus.Inliers.Select(track => (double)track.Dy));
        var residual = consensus.Inliers.Average(track =>
            Math.Sqrt(
                ((track.Dx - shiftX) * (track.Dx - shiftX))
                + ((track.Dy - shiftY) * (track.Dy - shiftY))));
        var averageTrackQuality = consensus.Inliers.Average(track => track.Quality);
        var coverageScore = Math.Clamp(
            Math.Min(
                (double)columnSpan / Math.Max(_settings.MinimumInlierColumnSpan, 1),
                (double)rowSpan / Math.Max(_settings.MinimumInlierRowSpan, 1)),
            0,
            1);
        var confidence = Math.Clamp(
            (inlierRatio * 0.35)
            + (texturedInlierRatio * 0.20)
            + (averageTrackQuality * 0.30)
            + (coverageScore * 0.15),
            0,
            1);
        if (confidence < _settings.MinimumReliableConfidence)
        {
            return VisualEgomotionEstimate.Unreliable(texturedFeatures, tracks.Count, brightnessDelta);
        }

        var isStationary = Math.Abs(shiftX) < _settings.MinimumHorizontalShiftPixels
            && Math.Abs(shiftY) < _settings.MinimumHorizontalShiftPixels;
        var horizontalDirection = Math.Abs(shiftX) < _settings.MinimumHorizontalShiftPixels
            ? VisualHorizontalDirection.None
            : shiftX < 0
                ? VisualHorizontalDirection.Right
                : VisualHorizontalDirection.Left;

        // Un déplacement du décor vers la gauche correspond à un mouvement de
        // caméra vers la droite. Cette conversion donne une valeur angulaire
        // utile sous l'hypothèse d'une rotation pure au centre de l'image.
        var focalLengthPixels = (width * 0.5)
            / Math.Tan(_horizontalFieldOfViewDegrees * Math.PI / 360.0);
        var yawDeltaDegrees = -Math.Atan(shiftX / focalLengthPixels) * 180.0 / Math.PI;

        return new VisualEgomotionEstimate(
            isStationary ? VisualEgomotionState.Stationary : VisualEgomotionState.Tracking,
            shiftX,
            shiftY,
            horizontalDirection,
            isStationary ? 0 : yawDeltaDegrees,
            confidence,
            texturedFeatures,
            tracks.Count,
            consensus.Inliers.Count,
            inlierRatio,
            residual,
            brightnessDelta);
    }

    private Consensus FindBestConsensus(IReadOnlyList<FeatureTrack> tracks)
    {
        var bestHypothesisIndex = -1;
        var bestCount = 0;
        var bestQuality = double.NegativeInfinity;
        for (var hypothesisIndex = 0; hypothesisIndex < tracks.Count; hypothesisIndex++)
        {
            var hypothesis = tracks[hypothesisIndex];
            var count = 0;
            var quality = 0.0;
            foreach (var track in tracks)
            {
                var dx = track.Dx - hypothesis.Dx;
                var dy = track.Dy - hypothesis.Dy;
                if ((dx * dx) + (dy * dy)
                    <= _settings.InlierRadiusPixels * _settings.InlierRadiusPixels)
                {
                    count++;
                    quality += track.Quality;
                }
            }

            if (count > bestCount || (count == bestCount && quality > bestQuality))
            {
                bestHypothesisIndex = hypothesisIndex;
                bestCount = count;
                bestQuality = quality;
            }
        }

        if (bestHypothesisIndex < 0)
        {
            return new Consensus([]);
        }

        var bestHypothesis = tracks[bestHypothesisIndex];
        var best = new List<FeatureTrack>(bestCount);
        foreach (var track in tracks)
        {
            var dx = track.Dx - bestHypothesis.Dx;
            var dy = track.Dy - bestHypothesis.Dy;
            if ((dx * dx) + (dy * dy)
                <= _settings.InlierRadiusPixels * _settings.InlierRadiusPixels)
            {
                best.Add(track);
            }
        }
        return new Consensus(best);
    }

    private bool TryFindUniqueMatch(
        ulong sourceDescriptor,
        ReadOnlySpan<byte> target,
        int width,
        int height,
        int sourceX,
        int sourceY,
        int horizontalRadius,
        int verticalRadius,
        out int bestDx,
        out int bestDy,
        out int bestDistance,
        out int uniquenessMargin)
    {
        bestDx = 0;
        bestDy = 0;
        bestDistance = int.MaxValue;
        var secondBestDistance = int.MaxValue;

        for (var dy = -verticalRadius; dy <= verticalRadius; dy++)
        {
            var y = sourceY + dy;
            for (var dx = -horizontalRadius; dx <= horizontalRadius; dx++)
            {
                var x = sourceX + dx;
                if (!TryCreateDescriptor(target, width, height, x, y, out var candidateDescriptor))
                {
                    continue;
                }

                var distance = BitOperations.PopCount(sourceDescriptor ^ candidateDescriptor);
                if (distance < bestDistance)
                {
                    secondBestDistance = bestDistance;
                    bestDistance = distance;
                    bestDx = dx;
                    bestDy = dy;
                }
                else if (distance < secondBestDistance)
                {
                    secondBestDistance = distance;
                }
            }
        }

        uniquenessMargin = secondBestDistance == int.MaxValue
            ? 0
            : secondBestDistance - bestDistance;
        return bestDistance <= _settings.MaximumDescriptorDistance
            && uniquenessMargin >= _settings.MinimumUniquenessMargin;
    }

    private bool TryCreateDescriptor(
        ReadOnlySpan<byte> frame,
        int width,
        int height,
        int centerX,
        int centerY,
        out ulong descriptor)
    {
        descriptor = 0;
        var radius = _settings.PatchRadius;
        if (centerX - radius < 0
            || centerY - radius < 0
            || centerX + radius >= width
            || centerY + radius >= height)
        {
            return false;
        }

        var minimum = byte.MaxValue;
        var maximum = byte.MinValue;
        var center = frame[(centerY * width) + centerX];
        var bit = 0;
        for (var y = centerY - radius; y <= centerY + radius; y++)
        {
            var rowOffset = y * width;
            for (var x = centerX - radius; x <= centerX + radius; x++)
            {
                var value = frame[rowOffset + x];
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                if (x == centerX && y == centerY)
                {
                    continue;
                }

                if (value >= center)
                {
                    descriptor |= 1UL << bit;
                }
                bit++;
            }
        }

        return maximum - minimum >= _settings.MinimumPatchRange;
    }

    private bool IsScenePatch(int centerX, int centerY, int width, int height)
    {
        var radius = _settings.PatchRadius;
        var left = (double)(centerX - radius) / width;
        var right = (double)(centerX + radius + 1) / width;
        var top = (double)(centerY - radius) / height;
        var bottom = (double)(centerY + radius + 1) / height;

        if (left < _settings.SideHudMaskFraction
            || right > 1 - _settings.SideHudMaskFraction)
        {
            return false;
        }

        var intersectsWeaponVertically = bottom > _settings.WeaponMaskTopFraction;
        var intersectsWeaponHorizontally = right > _settings.WeaponMaskLeftFraction
            && left < _settings.WeaponMaskRightFraction;
        return !(intersectsWeaponVertically && intersectsWeaponHorizontally)
            && top >= 0
            && bottom <= 1;
    }

    private void SaveFrame(ReadOnlySpan<byte> luminance, int width, int height)
    {
        if (_previousFrame.Length != luminance.Length)
        {
            _previousFrame = new byte[luminance.Length];
        }
        luminance.CopyTo(_previousFrame);
        _previousWidth = width;
        _previousHeight = height;
        _hasPreviousFrame = true;
    }

    private static int SampleCoordinate(int margin, int usableSize, int index, int count)
    {
        return margin + (((index * 2) + 1) * usableSize / (count * 2));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) * 0.5
            : sorted[middle];
    }

    private static double CalculateMean(ReadOnlySpan<byte> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
        }
        return (double)total / values.Length;
    }

    private static void ValidateFrame(ReadOnlySpan<byte> luminance, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height != luminance.Length)
        {
            throw new ArgumentException("Les dimensions de la trame de luminance sont invalides.", nameof(luminance));
        }
    }

    private static void ValidateSettings(VisualEgomotionSettings settings)
    {
        var descriptorBits = (((settings.PatchRadius * 2) + 1) * ((settings.PatchRadius * 2) + 1)) - 1;
        if (settings.PatchRadius < 1
            || descriptorBits is < 8 or > DescriptorBitCapacity
            || settings.HorizontalSearchRadius is < 1 or > 32
            || settings.VerticalSearchRadius is < 0 or > 16
            || settings.GridColumns is < 2 or > 32
            || settings.GridRows is < 2 or > 32
            || settings.MinimumPatchRange is < 1 or > 255
            || settings.MaximumDescriptorDistance < 0
            || settings.MaximumDescriptorDistance > descriptorBits
            || settings.MinimumUniquenessMargin < 0
            || settings.MinimumUniquenessMargin > descriptorBits
            || settings.MaximumForwardBackwardError < 0
            || settings.MinimumTexturedFeatures < 1
            || settings.MinimumTexturedFeatures > settings.GridColumns * settings.GridRows
            || settings.MinimumMatchedFeatures < 1
            || settings.MinimumMatchedFeatures > settings.MinimumTexturedFeatures
            || !double.IsFinite(settings.MinimumInlierRatio)
            || settings.MinimumInlierRatio is <= 0 or > 1
            || !double.IsFinite(settings.MinimumTexturedInlierRatio)
            || settings.MinimumTexturedInlierRatio is <= 0 or > 1
            || !double.IsFinite(settings.InlierRadiusPixels)
            || settings.InlierRadiusPixels < 0
            || settings.MinimumInlierColumnSpan < 1
            || settings.MinimumInlierRowSpan < 1
            || settings.MinimumInlierColumnSpan > settings.GridColumns
            || settings.MinimumInlierRowSpan > settings.GridRows
            || !double.IsFinite(settings.MinimumReliableConfidence)
            || settings.MinimumReliableConfidence is < 0 or > 1
            || !double.IsFinite(settings.MinimumHorizontalShiftPixels)
            || settings.MinimumHorizontalShiftPixels < 0
            || !IsFraction(settings.SideHudMaskFraction)
            || !IsFraction(settings.WeaponMaskTopFraction)
            || !IsFraction(settings.WeaponMaskLeftFraction)
            || !IsFraction(settings.WeaponMaskRightFraction)
            || settings.WeaponMaskLeftFraction >= settings.WeaponMaskRightFraction
            || !double.IsFinite(settings.HorizontalFieldOfViewDegrees)
            || settings.HorizontalFieldOfViewDegrees is <= 1 or >= 179)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Les réglages d'odométrie visuelle sont invalides.");
        }
    }

    private static bool IsFraction(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private int DescriptorBitCount => (((_settings.PatchRadius * 2) + 1) * ((_settings.PatchRadius * 2) + 1)) - 1;

    private readonly record struct FeatureTrack(
        int Dx,
        int Dy,
        int GridX,
        int GridY,
        double Quality);

    private readonly record struct Consensus(List<FeatureTrack> Inliers);
}
