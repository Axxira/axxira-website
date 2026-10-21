using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace RaidPilot.Overlay;

internal enum VisualMotionState
{
    Unknown,
    Stationary,
    Moving,
}

internal sealed record VisualMotionGateSettings
{
    internal int SampleWidth { get; init; } = 64;

    internal int SampleHeight { get; init; } = 36;

    internal double CropLeftFraction { get; init; } = 0.08;

    internal double CropTopFraction { get; init; } = 0.12;

    internal double CropWidthFraction { get; init; } = 0.84;

    internal double CropHeightFraction { get; init; } = 0.68;

    internal double MinimumTextureStandardDeviation { get; init; } = 10.0;

    internal double StationaryMeanDifferenceMaximum { get; init; } = 1.6;

    internal double StationaryChangedPixelRatioMaximum { get; init; } = 0.035;

    internal double MovingMeanDifferenceMinimum { get; init; } = 5.0;

    internal double MovingChangedPixelRatioMinimum { get; init; } = 0.16;

    internal double ChangedPixelDifference { get; init; } = 6.0;

    internal double MaximumStationaryBrightnessShift { get; init; } = 1.5;

    internal int MovingFramesRequired { get; init; } = 2;

    internal int StationaryFramesRequired { get; init; } = 4;
}

internal readonly record struct VisualMotionObservation(
    VisualMotionState State,
    VisualMotionState Evidence,
    double MotionScore,
    double MeanDifference,
    double ChangedPixelRatio,
    double TextureStandardDeviation,
    double BrightnessDelta,
    bool CaptureSucceeded,
    bool CaptureProvenLive,
    VisualEgomotionEstimate Egomotion);

/// <summary>
/// Observe une petite trame de luminance tirée de la fenêtre EFT. Le composant
/// ne donne jamais le focus à la fenêtre et ne crée aucun fichier. Son état
/// Stationary est volontairement difficile à atteindre: une capture doit
/// d'abord avoir montré du mouvement afin qu'une trame GDI figée ou noire ne
/// puisse pas bloquer à tort l'estimation de position.
/// </summary>
internal sealed class VisualMotionGate : IDisposable
{
    // La grille reste volontairement petite: elle doit distinguer un mouvement
    // réparti dans le décor d'une animation locale (arme, compteur, HUD), sans
    // transformer ce garde-fou en algorithme de vision coûteux.
    private const int MotionTileColumns = 8;
    private const int MotionTileRows = 4;
    private const double MinimumMovingTileRatio = 0.20;
    private const int MinimumMovingColumnSpan = 3;
    private const int MinimumMovingRowSpan = 2;

    private readonly object _sync = new();
    private readonly VisualMotionGateSettings _settings;
    private readonly byte[] _currentLuminance;
    private readonly byte[] _previousLuminance;
    private readonly byte[] _bgraPixels;
    private readonly VisualEgomotionEstimator _egomotionEstimator;
    private double? _verticalFieldOfViewDegrees;

    private bool _hasPreviousFrame;
    private bool _captureProvenLive;
    private VisualMotionState _state;
    private int _movingEvidenceFrames;
    private int _stationaryEvidenceFrames;
    private nint _captureWindow;
    private int _captureClientWidth;
    private int _captureClientHeight;

    private nint _memoryDc;
    private nint _dib;
    private nint _previousSelectedObject;
    private nint _dibPixels;
    private bool _disposed;

    internal VisualMotionGate(VisualMotionGateSettings? settings = null)
    {
        _settings = settings ?? new VisualMotionGateSettings();
        ValidateSettings(_settings);

        var pixelCount = checked(_settings.SampleWidth * _settings.SampleHeight);
        _currentLuminance = new byte[pixelCount];
        _previousLuminance = new byte[pixelCount];
        _bgraPixels = new byte[checked(pixelCount * 4)];
        _egomotionEstimator = new VisualEgomotionEstimator();
    }

    internal void ConfigureVerticalFieldOfView(double verticalFieldOfViewDegrees)
    {
        if (!EftGameViewSettings.IsValidVerticalFieldOfView(verticalFieldOfViewDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(verticalFieldOfViewDegrees));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            _verticalFieldOfViewDegrees = verticalFieldOfViewDegrees;
        }
    }

    internal double EgomotionHorizontalFieldOfViewDegrees =>
        _egomotionEstimator.HorizontalFieldOfViewDegrees;

    internal VisualMotionState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    internal bool CaptureProvenLive
    {
        get
        {
            lock (_sync)
            {
                return _captureProvenLive;
            }
        }
    }

    internal int SampleWidth => _settings.SampleWidth;

    internal int SampleHeight => _settings.SampleHeight;

    internal bool TryCopyLatestLuminance(out byte[] luminance)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_hasPreviousFrame)
            {
                luminance = Array.Empty<byte>();
                return false;
            }

            luminance = _currentLuminance.ToArray();
            return true;
        }
    }

    /// <summary>
    /// Produit exactement le même petit recadrage central depuis une capture
    /// EFT. L'image n'est jamais conservée; seul le tableau de luminance rendu
    /// au moteur de repères existe le temps de l'appelant.
    /// </summary>
    internal bool TryCreateLuminanceFrameFromImage(string path, out byte[] luminance)
    {
        luminance = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if (source.Width < _settings.SampleWidth || source.Height < _settings.SampleHeight)
            {
                return false;
            }

            var region = CalculateCaptureRegion(source.Width, source.Height, _settings);
            using var scaled = new Bitmap(
                _settings.SampleWidth,
                _settings.SampleHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, scaled.Width, scaled.Height),
                    new Rectangle(region.Left, region.Top, region.Width, region.Height),
                    GraphicsUnit.Pixel);
            }

            var result = new byte[checked(_settings.SampleWidth * _settings.SampleHeight)];
            for (var y = 0; y < scaled.Height; y++)
            {
                for (var x = 0; x < scaled.Width; x++)
                {
                    var color = scaled.GetPixel(x, y);
                    result[(y * scaled.Width) + x] = (byte)(
                        (color.R * 54 + color.G * 183 + color.B * 19 + 128) >> 8);
                }
            }

            luminance = result;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or ExternalException
            // GDI+ utilise aussi OutOfMemoryException pour signaler un flux
            // d'image incomplet ou invalide. Ici le fichier vient précisément
            // d'être créé par EFT; le considérer comme « pas encore prêt »
            // permet au retry borné ci-dessous de reprendre sans tuer l'UI.
            or OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Décode une capture nouvellement créée sans bloquer le thread UI. Un
    /// événement FileSystemWatcher peut précéder la fin d'écriture du PNG;
    /// quelques reprises courtes évitent de perdre définitivement le repère.
    /// </summary>
    internal async Task<byte[]?> CreateLuminanceFrameFromImageWithRetryAsync(
        string path,
        CancellationToken cancellationToken,
        int maximumAttempts = 5,
        TimeSpan? initialRetryDelay = null)
    {
        if (maximumAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        var retryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(60);
        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromSeconds(2))
        {
            throw new ArgumentOutOfRangeException(nameof(initialRetryDelay));
        }

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(
                () => TryCreateLuminanceFrameFromImage(path, out var luminance)
                    ? luminance
                    : null,
                cancellationToken);
            if (frame is not null)
            {
                return frame;
            }

            if (attempt + 1 < maximumAttempts)
            {
                var multiplier = attempt + 1;
                await Task.Delay(retryDelay * multiplier, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Capture la partie centrale large de la zone cliente de la fenêtre.
    /// StretchBlt réduit directement l'image à quelques milliers de pixels;
    /// aucune image pleine résolution n'est allouée ou conservée.
    /// </summary>
    internal bool TryObserveWindow(
        nint window,
        out VisualMotionObservation observation,
        bool estimateEgomotion = true)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!TryCaptureLuminance(window))
            {
                observation = ObserveCaptureFailure();
                return false;
            }

            observation = ObserveLuminanceFrameCore(_currentLuminance, estimateEgomotion);
            return true;
        }
    }

    /// <summary>
    /// Point d'entrée déterministe utilisé par les tests et réutilisable par
    /// une autre source locale de pixels. La trame doit être en luminance 8 bits.
    /// </summary>
    internal VisualMotionObservation ObserveLuminanceFrame(
        ReadOnlySpan<byte> luminance,
        int width,
        int height)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (width != _settings.SampleWidth || height != _settings.SampleHeight)
            {
                throw new ArgumentException(
                    $"La trame attendue mesure {_settings.SampleWidth}x{_settings.SampleHeight}.",
                    nameof(luminance));
            }

            if (luminance.Length != _currentLuminance.Length)
            {
                throw new ArgumentException("La taille de la trame de luminance est invalide.", nameof(luminance));
            }

            luminance.CopyTo(_currentLuminance);
            return ObserveLuminanceFrameCore(_currentLuminance, estimateEgomotion: true);
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ResetObservationStateCore(clearCaptureIdentity: true);
        }
    }

    /// <summary>
    /// Libère immédiatement un ancien arrêt confirmé quand une nouvelle
    /// commande de déplacement commence. La capture reste qualifiée de vivante.
    /// </summary>
    internal void ReleaseStationary()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_state == VisualMotionState.Stationary)
            {
                _state = VisualMotionState.Unknown;
            }
            _stationaryEvidenceFrames = 0;
        }
    }

    /// <summary>
    /// Oublie la preuve de capture vivante quand une entrée attendue ne produit
    /// aucun changement visuel. Une nouvelle séquence de mouvement devra la
    /// requalifier avant que Stationary puisse bloquer l'estimation.
    /// </summary>
    internal void InvalidateLiveness()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ResetObservationStateCore(clearCaptureIdentity: false);
        }
    }

    internal static VisualCaptureRegion CalculateCaptureRegion(
        int clientWidth,
        int clientHeight,
        VisualMotionGateSettings? settings = null)
    {
        var effectiveSettings = settings ?? new VisualMotionGateSettings();
        ValidateSettings(effectiveSettings);
        if (clientWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientWidth));
        }

        if (clientHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientHeight));
        }

        var left = Math.Clamp(
            (int)Math.Round(clientWidth * effectiveSettings.CropLeftFraction),
            0,
            clientWidth - 1);
        var top = Math.Clamp(
            (int)Math.Round(clientHeight * effectiveSettings.CropTopFraction),
            0,
            clientHeight - 1);
        var width = Math.Clamp(
            (int)Math.Round(clientWidth * effectiveSettings.CropWidthFraction),
            1,
            clientWidth - left);
        var height = Math.Clamp(
            (int)Math.Round(clientHeight * effectiveSettings.CropHeightFraction),
            1,
            clientHeight - top);
        return new VisualCaptureRegion(left, top, width, height);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            ReleaseGdiResources();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private VisualMotionObservation ObserveLuminanceFrameCore(
        ReadOnlySpan<byte> current,
        bool estimateEgomotion)
    {
        var egomotion = _egomotionEstimator.Observe(
            current,
            _settings.SampleWidth,
            _settings.SampleHeight,
            estimateEgomotion);
        var texture = CalculateStandardDeviation(current);
        if (!_hasPreviousFrame)
        {
            current.CopyTo(_previousLuminance);
            _hasPreviousFrame = true;
            return CreateObservation(
                VisualMotionState.Unknown,
                motionScore: 0,
                meanDifference: 0,
                changedPixelRatio: 0,
                texture,
                brightnessDelta: 0,
                captureSucceeded: true,
                egomotion);
        }

        var brightnessDelta = CalculateMean(current) - CalculateMean(_previousLuminance);
        var differenceTotal = 0.0;
        var changedPixels = 0;
        for (var index = 0; index < current.Length; index++)
        {
            // Retirer le déplacement moyen évite qu'une petite variation
            // d'exposition soit confondue avec un déplacement du décor.
            var difference = Math.Abs(current[index] - _previousLuminance[index] - brightnessDelta);
            differenceTotal += difference;
            if (difference >= _settings.ChangedPixelDifference)
            {
                changedPixels++;
            }
        }

        var meanDifference = differenceTotal / current.Length;
        var changedPixelRatio = (double)changedPixels / current.Length;
        var spatialMotion = CalculateSpatialMotion(
            current,
            _previousLuminance,
            brightnessDelta);
        var globalMotionScore = Math.Clamp(
            Math.Max(meanDifference / _settings.MovingMeanDifferenceMinimum,
                changedPixelRatio / _settings.MovingChangedPixelRatioMinimum),
            0,
            1);
        var spatialMotionScore = Math.Clamp(
            spatialMotion.ActiveSceneTileRatio / MinimumMovingTileRatio,
            0,
            1);
        var motionScore = globalMotionScore * spatialMotionScore;

        var evidence = ClassifyEvidence(
            texture,
            Math.Abs(brightnessDelta),
            meanDifference,
            changedPixelRatio,
            spatialMotion);

        // Un flux de décor cohérent est une preuve de mouvement plus forte que
        // les simples seuils pixel/tuiles. Il empêche surtout une petite rotation
        // ou un déplacement faible de devenir un faux Stationary.
        if (egomotion.State == VisualEgomotionState.Tracking && egomotion.IsReliable)
        {
            evidence = VisualMotionState.Moving;
        }

        current.CopyTo(_previousLuminance);
        ApplyHysteresis(evidence, egomotion);

        return CreateObservation(
            evidence,
            motionScore,
            meanDifference,
            changedPixelRatio,
            texture,
            brightnessDelta,
            captureSucceeded: true,
            egomotion);
    }

    private VisualMotionState ClassifyEvidence(
        double texture,
        double absoluteBrightnessDelta,
        double meanDifference,
        double changedPixelRatio,
        SpatialMotionMetrics spatialMotion)
    {
        // Une trame uniforme/noire est typique d'une capture GDI non prise en
        // charge par le mode d'affichage du jeu. Elle ne doit jamais devenir
        // une preuve que le joueur est immobile.
        if (texture < _settings.MinimumTextureStandardDeviation)
        {
            return VisualMotionState.Unknown;
        }

        var globalMovingThresholdReached = meanDifference >= _settings.MovingMeanDifferenceMinimum
            || changedPixelRatio >= _settings.MovingChangedPixelRatioMinimum;
        if (globalMovingThresholdReached && spatialMotion.IsDistributed)
        {
            return VisualMotionState.Moving;
        }

        // Une animation confinée à quelques tuiles ne prouve pas que la caméra
        // se déplace. Elle interdit toutefois Stationary: bloquer l'odométrie
        // sur une scène qui change serait plus grave qu'une courte zone Unknown.
        if (spatialMotion.HasLocalizedActivity)
        {
            return VisualMotionState.Unknown;
        }

        if (absoluteBrightnessDelta <= _settings.MaximumStationaryBrightnessShift
            && !spatialMotion.HasLocalizedActivity)
        {
            return VisualMotionState.Stationary;
        }

        return VisualMotionState.Unknown;
    }

    private SpatialMotionMetrics CalculateSpatialMotion(
        ReadOnlySpan<byte> current,
        ReadOnlySpan<byte> previous,
        double brightnessDelta)
    {
        var columns = Math.Min(MotionTileColumns, _settings.SampleWidth);
        var rows = Math.Min(MotionTileRows, _settings.SampleHeight);
        // Fixed-size stack buffers avoid two managed allocations on every
        // visual sample (normally ten times per second during a raid).
        Span<bool> activeColumns = stackalloc bool[columns];
        Span<bool> activeRows = stackalloc bool[rows];
        var eligibleSceneTiles = 0;
        var activeSceneTiles = 0;
        var hasLocalizedActivity = false;

        for (var tileY = 0; tileY < rows; tileY++)
        {
            var yStart = tileY * _settings.SampleHeight / rows;
            var yEnd = (tileY + 1) * _settings.SampleHeight / rows;
            for (var tileX = 0; tileX < columns; tileX++)
            {
                var xStart = tileX * _settings.SampleWidth / columns;
                var xEnd = (tileX + 1) * _settings.SampleWidth / columns;
                var differenceTotal = 0.0;
                var changedPixels = 0;
                var pixelCount = 0;

                for (var y = yStart; y < yEnd; y++)
                {
                    var rowOffset = y * _settings.SampleWidth;
                    for (var x = xStart; x < xEnd; x++)
                    {
                        var index = rowOffset + x;
                        var difference = Math.Abs(current[index] - previous[index] - brightnessDelta);
                        differenceTotal += difference;
                        if (difference >= _settings.ChangedPixelDifference)
                        {
                            changedPixels++;
                        }
                        pixelCount++;
                    }
                }

                var tileMeanDifference = differenceTotal / pixelCount;
                var tileChangedPixelRatio = (double)changedPixels / pixelCount;
                if (!IsSceneTile(tileX, tileY, columns, rows))
                {
                    continue;
                }

                eligibleSceneTiles++;
                if (tileMeanDifference > _settings.StationaryMeanDifferenceMaximum
                    || tileChangedPixelRatio > _settings.StationaryChangedPixelRatioMaximum)
                {
                    // Le HUD et la zone de l'arme sont exclus avant ce test :
                    // leur animation ne doit ni simuler un déplacement, ni
                    // empêcher un vrai repos de déclencher le recalage F8.
                    hasLocalizedActivity = true;
                }
                if (tileMeanDifference >= _settings.MovingMeanDifferenceMinimum
                    || tileChangedPixelRatio >= _settings.MovingChangedPixelRatioMinimum)
                {
                    activeSceneTiles++;
                    activeColumns[tileX] = true;
                    activeRows[tileY] = true;
                }
            }
        }

        var activeSceneTileRatio = eligibleSceneTiles == 0
            ? 0
            : (double)activeSceneTiles / eligibleSceneTiles;
        var activeColumnCount = 0;
        foreach (var active in activeColumns)
        {
            if (active)
            {
                activeColumnCount++;
            }
        }
        var activeRowCount = 0;
        foreach (var active in activeRows)
        {
            if (active)
            {
                activeRowCount++;
            }
        }
        var isDistributed = activeSceneTileRatio >= MinimumMovingTileRatio
            && activeColumnCount >= Math.Min(MinimumMovingColumnSpan, columns)
            && activeRowCount >= Math.Min(MinimumMovingRowSpan, rows);

        return new SpatialMotionMetrics(
            activeSceneTileRatio,
            hasLocalizedActivity,
            isDistributed);
    }

    private static bool IsSceneTile(int tileX, int tileY, int columns, int rows)
    {
        // Les colonnes extrêmes concentrent les panneaux de statut. La zone
        // centrale de la dernière rangée est celle où l'arme et les mains
        // animent le plus souvent des pixels sans déplacement du joueur.
        if (columns >= 4 && (tileX == 0 || tileX == columns - 1))
        {
            return false;
        }

        var normalizedX = (tileX + 0.5) / columns;
        var isLowerCenterWeaponRegion = tileY == rows - 1
            && normalizedX is >= 0.25 and <= 0.75;
        return !isLowerCenterWeaponRegion;
    }

    private void ApplyHysteresis(
        VisualMotionState evidence,
        VisualEgomotionEstimate egomotion)
    {
        switch (evidence)
        {
            case VisualMotionState.Moving:
                _movingEvidenceFrames++;
                _stationaryEvidenceFrames = 0;

                // Quitter immédiatement Stationary est prudent: une seule
                // trame de mouvement suffit à ne plus bloquer le déplacement.
                if (_state == VisualMotionState.Stationary)
                {
                    _state = VisualMotionState.Unknown;
                }

                if (_movingEvidenceFrames >= _settings.MovingFramesRequired)
                {
                    _captureProvenLive = true;
                    _state = VisualMotionState.Moving;
                }
                break;

            case VisualMotionState.Stationary:
                _movingEvidenceFrames = 0;
                // Quand les deux détecteurs indépendants (différence spatiale
                // et suivi de points) voient la même scène immobile, compter
                // deux preuves réduit l'overshoot contre un mur sans accepter
                // une image figée: CaptureProvenLive reste obligatoire.
                _stationaryEvidenceFrames +=
                    egomotion.State == VisualEgomotionState.Stationary && egomotion.IsReliable
                        ? 2
                        : 1;
                if (_captureProvenLive
                    && _stationaryEvidenceFrames >= _settings.StationaryFramesRequired)
                {
                    _state = VisualMotionState.Stationary;
                }
                break;

            default:
                _movingEvidenceFrames = 0;
                _stationaryEvidenceFrames = 0;
                _state = VisualMotionState.Unknown;
                break;
        }
    }

    private VisualMotionObservation ObserveCaptureFailure()
    {
        ResetObservationStateCore(clearCaptureIdentity: true);
        return new VisualMotionObservation(
            State: VisualMotionState.Unknown,
            Evidence: VisualMotionState.Unknown,
            MotionScore: 0,
            MeanDifference: 0,
            ChangedPixelRatio: 0,
            TextureStandardDeviation: 0,
            BrightnessDelta: 0,
            CaptureSucceeded: false,
            CaptureProvenLive: false,
            Egomotion: VisualEgomotionEstimate.Unreliable(0, 0, 0));
    }

    private VisualMotionObservation CreateObservation(
        VisualMotionState evidence,
        double motionScore,
        double meanDifference,
        double changedPixelRatio,
        double texture,
        double brightnessDelta,
        bool captureSucceeded,
        VisualEgomotionEstimate egomotion)
    {
        return new VisualMotionObservation(
            State: _state,
            Evidence: evidence,
            MotionScore: motionScore,
            MeanDifference: meanDifference,
            ChangedPixelRatio: changedPixelRatio,
            TextureStandardDeviation: texture,
            BrightnessDelta: brightnessDelta,
            CaptureSucceeded: captureSucceeded,
            CaptureProvenLive: _captureProvenLive,
            Egomotion: egomotion);
    }

    private bool TryCaptureLuminance(nint window)
    {
        if (window == nint.Zero
            || !GdiCaptureMethods.IsWindow(window)
            || GdiCaptureMethods.IsIconic(window)
            || !GdiCaptureMethods.GetClientRect(window, out var clientRect))
        {
            return false;
        }

        var clientWidth = clientRect.Right - clientRect.Left;
        var clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth < _settings.SampleWidth || clientHeight < _settings.SampleHeight)
        {
            return false;
        }

        if (_captureWindow != window
            || _captureClientWidth != clientWidth
            || _captureClientHeight != clientHeight)
        {
            ResetObservationStateCore(clearCaptureIdentity: false);
            _captureWindow = window;
            _captureClientWidth = clientWidth;
            _captureClientHeight = clientHeight;
        }

        if (!EnsureGdiResources())
        {
            return false;
        }

        var sourceDc = GdiCaptureMethods.GetDC(window);
        if (sourceDc == nint.Zero)
        {
            return false;
        }

        try
        {
            var region = CalculateCaptureRegion(clientWidth, clientHeight, _settings);
            if (_verticalFieldOfViewDegrees is { } verticalFov)
            {
                var aspectRatio = clientWidth / (double)clientHeight;
                var horizontalCropFraction = region.Width / (double)clientWidth;
                var effectiveHorizontalFov =
                    EftGameViewSettings.CalculateEffectiveHorizontalFieldOfViewDegrees(
                        verticalFov,
                        aspectRatio,
                        horizontalCropFraction);
                _egomotionEstimator.SetHorizontalFieldOfViewDegrees(effectiveHorizontalFov);
            }

            if (!GdiCaptureMethods.StretchBlt(
                    _memoryDc,
                    0,
                    0,
                    _settings.SampleWidth,
                    _settings.SampleHeight,
                    sourceDc,
                    region.Left,
                    region.Top,
                    region.Width,
                    region.Height,
                    GdiCaptureMethods.SourceCopy))
            {
                return false;
            }

            GdiCaptureMethods.GdiFlush();
            Marshal.Copy(_dibPixels, _bgraPixels, 0, _bgraPixels.Length);
            for (var pixel = 0; pixel < _currentLuminance.Length; pixel++)
            {
                var offset = pixel * 4;
                var blue = _bgraPixels[offset];
                var green = _bgraPixels[offset + 1];
                var red = _bgraPixels[offset + 2];
                _currentLuminance[pixel] = (byte)((red * 54 + green * 183 + blue * 19 + 128) >> 8);
            }
            return true;
        }
        finally
        {
            GdiCaptureMethods.ReleaseDC(window, sourceDc);
        }
    }

    private bool EnsureGdiResources()
    {
        if (_memoryDc != nint.Zero)
        {
            return true;
        }

        _memoryDc = GdiCaptureMethods.CreateCompatibleDC(nint.Zero);
        if (_memoryDc == nint.Zero)
        {
            return false;
        }

        var bitmapInfo = new GdiCaptureMethods.BitmapInfo
        {
            Header = new GdiCaptureMethods.BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<GdiCaptureMethods.BitmapInfoHeader>(),
                Width = _settings.SampleWidth,
                Height = -_settings.SampleHeight,
                Planes = 1,
                BitCount = 32,
                Compression = GdiCaptureMethods.BiRgb,
            },
        };
        _dib = GdiCaptureMethods.CreateDIBSection(
            _memoryDc,
            in bitmapInfo,
            GdiCaptureMethods.DibRgbColors,
            out _dibPixels,
            nint.Zero,
            0);
        if (_dib == nint.Zero || _dibPixels == nint.Zero)
        {
            ReleaseGdiResources();
            return false;
        }

        _previousSelectedObject = GdiCaptureMethods.SelectObject(_memoryDc, _dib);
        if (_previousSelectedObject == nint.Zero
            || _previousSelectedObject == GdiCaptureMethods.HgdiError)
        {
            ReleaseGdiResources();
            return false;
        }

        GdiCaptureMethods.SetStretchBltMode(_memoryDc, GdiCaptureMethods.ColorOnColor);

        return true;
    }

    private void ResetObservationStateCore(bool clearCaptureIdentity)
    {
        _hasPreviousFrame = false;
        _captureProvenLive = false;
        _state = VisualMotionState.Unknown;
        _movingEvidenceFrames = 0;
        _stationaryEvidenceFrames = 0;
        Array.Clear(_previousLuminance);
        _egomotionEstimator.Reset();
        if (clearCaptureIdentity)
        {
            _captureWindow = nint.Zero;
            _captureClientWidth = 0;
            _captureClientHeight = 0;
        }
    }

    private void ReleaseGdiResources()
    {
        if (_memoryDc != nint.Zero
            && _previousSelectedObject != nint.Zero
            && _previousSelectedObject != GdiCaptureMethods.HgdiError)
        {
            GdiCaptureMethods.SelectObject(_memoryDc, _previousSelectedObject);
        }

        _previousSelectedObject = nint.Zero;
        if (_dib != nint.Zero)
        {
            GdiCaptureMethods.DeleteObject(_dib);
            _dib = nint.Zero;
        }
        _dibPixels = nint.Zero;

        if (_memoryDc != nint.Zero)
        {
            GdiCaptureMethods.DeleteDC(_memoryDc);
            _memoryDc = nint.Zero;
        }
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

    private static double CalculateStandardDeviation(ReadOnlySpan<byte> values)
    {
        var mean = CalculateMean(values);
        var squaredDifferenceTotal = 0.0;
        foreach (var value in values)
        {
            var difference = value - mean;
            squaredDifferenceTotal += difference * difference;
        }
        return Math.Sqrt(squaredDifferenceTotal / values.Length);
    }

    private static void ValidateSettings(VisualMotionGateSettings settings)
    {
        if (settings.SampleWidth is < 8 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.SampleWidth));
        }
        if (settings.SampleHeight is < 8 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.SampleHeight));
        }
        if (!double.IsFinite(settings.CropLeftFraction)
            || !double.IsFinite(settings.CropTopFraction)
            || !double.IsFinite(settings.CropWidthFraction)
            || !double.IsFinite(settings.CropHeightFraction)
            || settings.CropLeftFraction is < 0 or >= 1
            || settings.CropTopFraction is < 0 or >= 1
            || settings.CropWidthFraction is <= 0 or > 1
            || settings.CropHeightFraction is <= 0 or > 1
            || settings.CropLeftFraction + settings.CropWidthFraction > 1
            || settings.CropTopFraction + settings.CropHeightFraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "La zone de capture doit rester dans la fenêtre.");
        }
        if (!double.IsFinite(settings.MinimumTextureStandardDeviation)
            || !double.IsFinite(settings.StationaryMeanDifferenceMaximum)
            || !double.IsFinite(settings.StationaryChangedPixelRatioMaximum)
            || !double.IsFinite(settings.MovingMeanDifferenceMinimum)
            || !double.IsFinite(settings.MovingChangedPixelRatioMinimum)
            || !double.IsFinite(settings.ChangedPixelDifference)
            || !double.IsFinite(settings.MaximumStationaryBrightnessShift)
            || settings.MinimumTextureStandardDeviation < 0
            || settings.StationaryMeanDifferenceMaximum < 0
            || settings.StationaryChangedPixelRatioMaximum is < 0 or > 1
            || settings.MovingMeanDifferenceMinimum <= settings.StationaryMeanDifferenceMaximum
            || settings.MovingChangedPixelRatioMinimum is <= 0 or > 1
            || settings.MovingChangedPixelRatioMinimum <= settings.StationaryChangedPixelRatioMaximum
            || settings.ChangedPixelDifference <= 0
            || settings.MaximumStationaryBrightnessShift < 0
            || settings.MovingFramesRequired < 1
            || settings.StationaryFramesRequired < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Les seuils de mouvement sont invalides.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VisualMotionGate));
        }
    }

    private readonly record struct SpatialMotionMetrics(
        double ActiveSceneTileRatio,
        bool HasLocalizedActivity,
        bool IsDistributed);

    internal static class GdiCaptureMethods
    {
        internal const uint SourceCopy = 0x00CC0020;
        internal const int ColorOnColor = 3;
        internal const uint DibRgbColors = 0;
        internal const uint BiRgb = 0;
        internal static readonly nint HgdiError = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(nint window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint GetDC(nint window);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint window, nint dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateCompatibleDC(nint dc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(nint dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateDIBSection(
            nint dc,
            in BitmapInfo bitmapInfo,
            uint usage,
            out nint bits,
            nint section,
            uint offset);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint dc, nint objectHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint objectHandle);

        [DllImport("gdi32.dll")]
        internal static extern int SetStretchBltMode(nint dc, int mode);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool StretchBlt(
            nint destinationDc,
            int destinationX,
            int destinationY,
            int destinationWidth,
            int destinationHeight,
            nint sourceDc,
            int sourceX,
            int sourceY,
            int sourceWidth,
            int sourceHeight,
            uint rasterOperation);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GdiFlush();
    }
}

internal readonly record struct VisualCaptureRegion(int Left, int Top, int Width, int Height);