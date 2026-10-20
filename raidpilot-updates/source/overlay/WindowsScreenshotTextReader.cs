using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RaidPilot.Overlay;

/// <summary>
/// Lit localement la zone du panneau d'extractions d'une capture EFT. Les
/// pixels et lignes OCR brutes ne sont ni téléversés ni enregistrés.
/// </summary>
internal sealed class WindowsScreenshotTextReader
{
    // Windows OCR and bitmap decoding are CPU-heavy and can otherwise run
    // three times in parallel for one F8 (extractions, timer and quests). One
    // reader is shared by the overlay, so this gate keeps those short bursts
    // serialized and leaves more CPU/GPU time to Tarkov.
    private readonly SemaphoreSlim _processingGate = new(1, 1);

    internal sealed record OcrTextFragment(
        string Text,
        double X,
        double Y,
        double Width,
        double Height);

    private const int ReadyProbeCount = 30;
    private const int RequiredStableProbes = 3;
    private const int DecodeAttemptCount = 16;
    private const int RaidTimerTemplateWidth = 8;
    private const int RaidTimerTemplateHeight = 12;
    private const int RaidTimerMaximumTemplateDistance = 16;
    private const int RaidTimerMinimumTemplateMargin = 4;
    private static readonly TimeSpan ReadyProbeDelay = TimeSpan.FromMilliseconds(120);
    private static readonly (int Digit, string Pixels)[] RaidTimerDigitTemplates =
    [
        (0,
            "01111110" +
            "11111111" +
            "11101111" +
            "11001111" +
            "11011111" +
            "11011111" +
            "11111011" +
            "11110011" +
            "11110011" +
            "11110111" +
            "11111111" +
            "01111110"),
        (2,
            "11111110" +
            "11111111" +
            "11000111" +
            "00000011" +
            "00000011" +
            "00000111" +
            "00011110" +
            "00111100" +
            "11110000" +
            "11110000" +
            "11111111" +
            "11111111"),
        (3,
            "11111111" +
            "11111111" +
            "00001111" +
            "00001110" +
            "00011100" +
            "00111110" +
            "00111111" +
            "00000011" +
            "00000011" +
            "11000111" +
            "11111111" +
            "11111110"),
        (4,
            "00000110" +
            "00001110" +
            "00011110" +
            "00011110" +
            "00111110" +
            "01110110" +
            "11100110" +
            "11111111" +
            "11111111" +
            "11111110" +
            "00000110" +
            "00000110"),
        (5,
            "11111111" +
            "11111111" +
            "11100000" +
            "11100000" +
            "11111100" +
            "11111110" +
            "11111111" +
            "00000011" +
            "00000011" +
            "01000111" +
            "11111111" +
            "11111110"),
        (6,
            "01111111" +
            "11111111" +
            "11100011" +
            "11000000" +
            "11111100" +
            "11111110" +
            "11111111" +
            "11000011" +
            "11000011" +
            "11100111" +
            "11111110" +
            "01111110"),
        (7,
            "11111111" +
            "11111111" +
            "00000111" +
            "00000111" +
            "00000110" +
            "00001110" +
            "00001100" +
            "00011100" +
            "00011000" +
            "00111000" +
            "00111000" +
            "00111000"),
        (8,
            "01111110" +
            "11111110" +
            "11100111" +
            "11000011" +
            "11100111" +
            "11111110" +
            "11111111" +
            "11000111" +
            "11000011" +
            "11100111" +
            "11111110" +
            "01111110"),
        (9,
            "01111110" +
            "11111111" +
            "11000111" +
            "11000011" +
            "11000011" +
            "11111111" +
            "01111111" +
            "00111111" +
            "00000011" +
            "11111111" +
            "11111111" +
            "01111100"),
    ];

    internal async Task<IReadOnlyList<string>> ReadExtractionPanelLinesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        await WaitForCompleteFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < DecodeAttemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ReadOnceAsync(fullPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException
                    or COMException
                    or UnauthorizedAccessException)
                {
                    lastFailure = exception;
                    if (attempt < DecodeAttemptCount - 1)
                    {
                        await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new IOException("La capture Tarkov n'a pas pu être lue complètement.", lastFailure);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    internal async Task<IReadOnlyList<string>> ReadQuestPanelLinesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        await WaitForCompleteFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < DecodeAttemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ReadQuestOnceAsync(fullPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException
                    or COMException
                    or UnauthorizedAccessException)
                {
                    lastFailure = exception;
                    if (attempt < DecodeAttemptCount - 1)
                    {
                        await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new IOException("La capture Tarkov n'a pas pu être lue complètement.", lastFailure);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    internal async Task<IReadOnlyList<string>> ReadRaidTimerLinesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        await WaitForCompleteFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await _processingGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Exception? lastFailure = null;
            for (var attempt = 0; attempt < DecodeAttemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ReadRaidTimerOnceAsync(fullPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException
                    or COMException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or OutOfMemoryException)
                {
                    lastFailure = exception;
                    if (attempt < DecodeAttemptCount - 1)
                    {
                        await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new IOException("Le chrono de la capture Tarkov n'a pas pu être lu complètement.", lastFailure);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadOnceAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var randomAccessStream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

        // EFT dessine le panneau O dans le coin supérieur droit. Le rogner
        // augmente fortement la précision et évite que les noms de lieux ou le
        // HUD deviennent des faux positifs.
        var panelBounds = CalculateExtractionPanelBounds(
            decoder.PixelWidth,
            decoder.PixelHeight);
        var transform = new BitmapTransform
        {
            Bounds = panelBounds,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        cancellationToken.ThrowIfCancellationRequested();

        var engines = CreateEngines();
        if (engines.Count == 0)
        {
            throw new NotSupportedException("Aucun module OCR Windows n'est installé.");
        }

        IReadOnlyList<string> bestLines = Array.Empty<string>();
        var bestSlotCount = -1;
        var bestTextLength = -1;
        foreach (var engine in engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            var fragments = result.Lines
                .Select(line =>
                {
                    var words = line.Words;
                    var left = words.Count == 0 ? 0 : words.Min(word => word.BoundingRect.X);
                    var top = words.Count == 0 ? 0 : words.Min(word => word.BoundingRect.Y);
                    var right = words.Count == 0 ? 0 : words.Max(word =>
                        word.BoundingRect.X + word.BoundingRect.Width);
                    var bottom = words.Count == 0 ? 0 : words.Max(word =>
                        word.BoundingRect.Y + word.BoundingRect.Height);
                    return new OcrTextFragment(
                        line.Text?.Trim() ?? string.Empty,
                        left,
                        top,
                        Math.Max(0, right - left),
                        Math.Max(0, bottom - top));
                })
                .Where(fragment => fragment.Text.Length > 0)
                .ToArray();
            var lines = MergeRowFragments(fragments);

            var uniqueExfilLines = ExtractionAdvisor.SelectUniqueOcrExfilLines(lines);
            var textLength = uniqueExfilLines.Sum(line => line.Length);
            if (uniqueExfilLines.Count > bestSlotCount
                || (uniqueExfilLines.Count == bestSlotCount && textLength > bestTextLength))
            {
                bestLines = lines.ToArray();
                bestSlotCount = uniqueExfilLines.Count;
                bestTextLength = textLength;
            }
        }

        return bestLines;
    }

    private static async Task<IReadOnlyList<string>> ReadQuestOnceAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var randomAccessStream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        var transform = new BitmapTransform
        {
            Bounds = CalculateQuestPanelBounds(decoder.PixelWidth, decoder.PixelHeight),
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        cancellationToken.ThrowIfCancellationRequested();

        var engines = CreateEngines();
        if (engines.Count == 0)
        {
            throw new NotSupportedException("Aucun module OCR Windows n'est installé.");
        }

        // Les noms de quêtes peuvent être en français ou en anglais. Fusionner
        // les lectures des moteurs disponibles augmente le rappel; l'appariement
        // strict et non ambigu de QuestAdvisor élimine ensuite le bruit.
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var engine in engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            var fragments = result.Lines
                .Select(line =>
                {
                    var words = line.Words;
                    var left = words.Count == 0 ? 0 : words.Min(word => word.BoundingRect.X);
                    var top = words.Count == 0 ? 0 : words.Min(word => word.BoundingRect.Y);
                    var right = words.Count == 0 ? 0 : words.Max(word =>
                        word.BoundingRect.X + word.BoundingRect.Width);
                    var bottom = words.Count == 0 ? 0 : words.Max(word =>
                        word.BoundingRect.Y + word.BoundingRect.Height);
                    return new OcrTextFragment(
                        line.Text?.Trim() ?? string.Empty,
                        left,
                        top,
                        Math.Max(0, right - left),
                        Math.Max(0, bottom - top));
                })
                .Where(fragment => fragment.Text.Length > 0)
                .ToArray();
            foreach (var line in MergeRowFragments(fragments))
            {
                if (seen.Add(line))
                {
                    lines.Add(line);
                }
            }
        }

        return lines;
    }

    private static async Task<IReadOnlyList<string>> ReadRaidTimerOnceAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var input = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: false);
        using var source = new System.Drawing.Bitmap(input);
        var timerBounds = CalculateRaidTimerBounds(
            checked((uint)source.Width),
            checked((uint)source.Height));
        var tightTimerBounds = CalculateRaidTimerTightBounds(
            checked((uint)source.Width),
            checked((uint)source.Height));
        if (TryReadRaidTimerFromPixels(source, tightTimerBounds, out var pixelSeconds)
            || TryReadRaidTimerFromPixels(source, timerBounds, out pixelSeconds))
        {
            return
            [
                FormattableString.Invariant(
                    $"RAID TIME {pixelSeconds / 3600}:{(pixelSeconds / 60) % 60:00}:{pixelSeconds % 60:00}"),
            ];
        }

        var headerBounds = CalculateRaidTimerHeaderBounds(
            checked((uint)source.Width),
            checked((uint)source.Height));
        var engines = CreateEngines();
        if (engines.Count == 0)
        {
            throw new NotSupportedException("Aucun module OCR Windows n'est installé.");
        }

        var combinedLines = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var variants = new (BitmapBounds Bounds, double Scale, bool Monochrome, bool RedMask)[]
        {
            (headerBounds, 3.0, false, false),
            (tightTimerBounds, 4.0, false, false),
            (tightTimerBounds, 4.0, true, false),
            (tightTimerBounds, 4.0, false, true),
            (timerBounds, 4.0, true, false),
        };
        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var rendered = RenderRaidTimerVariant(
                source,
                variant.Bounds,
                variant.Scale,
                variant.Monochrome,
                variant.RedMask);
            using var bitmap = await ConvertToSoftwareBitmapAsync(rendered);
            foreach (var engine in engines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
                var lines = result.Lines
                    .Select(line => line.Text?.Trim() ?? string.Empty)
                    .Where(line => line.Length > 0)
                    .ToArray();

                // Dès qu'un moteur fournit un chrono valide, conserver sa
                // lecture cohérente plutôt que de mélanger les confusions de
                // plusieurs langues et variantes.
                if (RaidTimerParser.TryParseRemainingSeconds(lines, out _))
                {
                    return lines;
                }

                foreach (var line in lines)
                {
                    if (seen.Add(line))
                    {
                        combinedLines.Add(line);
                    }
                }
            }
        }

        return combinedLines;
    }

    private static bool TryReadRaidTimerFromPixels(
        System.Drawing.Bitmap source,
        BitmapBounds bounds,
        out int remainingSeconds)
    {
        // En fin de raid, Tarkov colore le chrono en rouge. Cette passe doit
        // précéder la passe claire: la luminance du rouge pur est trop basse
        // pour le masque blanc et le décor visible sous le panneau peut être
        // plus lumineux que les chiffres.
        if (TryReadRaidTimerFromPixels(source, bounds, redOnly: true, out remainingSeconds))
        {
            return true;
        }

        return TryReadRaidTimerFromPixels(source, bounds, redOnly: false, out remainingSeconds);
    }

    private static bool TryReadRaidTimerFromPixels(
        System.Drawing.Bitmap source,
        BitmapBounds bounds,
        bool redOnly,
        out int remainingSeconds)
    {
        remainingSeconds = 0;
        var left = checked((int)bounds.X);
        var top = checked((int)bounds.Y);
        var width = checked((int)bounds.Width);
        var height = checked((int)bounds.Height);
        var rawRuns = new List<TimerGlyphRun>();
        var minimumColumnInk = Math.Max(2, height / 12);
        int? runStart = null;
        for (var localX = 0; localX < width; localX++)
        {
            var inkRows = 0;
            for (var localY = 0; localY < height; localY++)
            {
                if (IsRaidTimerInk(source.GetPixel(left + localX, top + localY), redOnly)
                    && ++inkRows >= minimumColumnInk)
                {
                    break;
                }
            }
            var hasInk = inkRows >= minimumColumnInk;

            if (hasInk && runStart is null)
            {
                runStart = localX;
            }
            else if (!hasInk && runStart is not null)
            {
                rawRuns.Add(new TimerGlyphRun(runStart.Value, localX - 1));
                runStart = null;
            }
        }
        if (runStart is not null)
        {
            rawRuns.Add(new TimerGlyphRun(runStart.Value, width - 1));
        }

        // Les bordures du panneau et quelques pixels du décor peuvent créer un
        // petit run avant/après le chrono. N'accepter qu'une UNIQUE fenêtre de
        // sept glyphes qui respecte H:MM:SS; aucune sous-chaîne ambiguë n'est
        // choisie arbitrairement.
        if (!TrySelectRaidTimerRunWidths(
                rawRuns.Select(run => run.Width).ToArray(),
                out var selectedStart))
        {
            return false;
        }
        var runs = rawRuns.Skip(selectedStart).Take(7).ToList();

        var digitRuns = new[] { runs[0], runs[2], runs[3], runs[5], runs[6] };
        // La médiane devient la largeur étroite du 1 dans 0:11:11. Le plus
        // grand glyphe reste, lui, une référence fiable pour la largeur pleine
        // (le zéro des heures en conditions EFT normales).
        var referenceDigitWidth = digitRuns.Max(run => run.Width);
        if (referenceDigitWidth < 3
            || runs[1].Width > referenceDigitWidth * 0.55
            || runs[4].Width > referenceDigitWidth * 0.55)
        {
            return false;
        }

        if (!TryGetTimerDigitBand(
            source,
            left,
            top,
            digitRuns,
            height,
            referenceDigitWidth,
            redOnly,
            out var digitTop,
            out var digitBottom))
        {
            return false;
        }
        var digitHeight = digitBottom - digitTop + 1;
        if (digitHeight < 5
            || !TryGetTimerInkBounds(source, left, top, runs[1], digitTop, digitBottom, redOnly, out var firstColonTop, out var firstColonBottom)
            || !TryGetTimerInkBounds(source, left, top, runs[4], digitTop, digitBottom, redOnly, out var secondColonTop, out var secondColonBottom)
            || firstColonBottom - firstColonTop + 1 > digitHeight * 0.85
            || secondColonBottom - secondColonTop + 1 > digitHeight * 0.85)
        {
            return false;
        }

        string? sameImageZeroTemplate = null;
        if (digitRuns[0].Width > referenceDigitWidth * 0.72)
        {
            var candidate = BuildRaidTimerGlyphTemplate(
                source,
                left,
                top,
                digitRuns[0],
                digitTop,
                digitBottom,
                redOnly);
            var zeroTemplate = RaidTimerDigitTemplates[0].Pixels;
            var zeroDistance = MeasureRaidTimerTemplateDistance(candidate, zeroTemplate);
            if (zeroDistance <= (redOnly ? 24 : RaidTimerMaximumTemplateDistance))
            {
                // L'heure est zéro pour les raids EFT actuels. Son glyphe
                // fournit donc un zéro de référence ayant exactement la même
                // couleur, taille et anticrénelage que les autres chiffres.
                sameImageZeroTemplate = candidate;
            }
        }

        Span<int> digits = stackalloc int[5];
        for (var index = 0; index < digitRuns.Length; index++)
        {
            if (!TryClassifyRaidTimerDigit(
                source,
                left,
                top,
                digitRuns[index],
                digitTop,
                digitBottom,
                referenceDigitWidth,
                redOnly,
                sameImageZeroTemplate,
                out digits[index]))
            {
                return false;
            }
        }

        var hours = digits[0];
        var minutes = (digits[1] * 10) + digits[2];
        var seconds = (digits[3] * 10) + digits[4];
        if (hours > 2 || minutes >= 60 || seconds >= 60)
        {
            return false;
        }

        remainingSeconds = (hours * 3600) + (minutes * 60) + seconds;
        return remainingSeconds <= RaidTimerParser.MaximumRemainingSeconds;
    }

    private static bool TryClassifyRaidTimerDigit(
        System.Drawing.Bitmap source,
        int cropLeft,
        int cropTop,
        TimerGlyphRun run,
        int digitTop,
        int digitBottom,
        int referenceDigitWidth,
        bool redOnly,
        string? sameImageZeroTemplate,
        out int digit)
    {
        digit = -1;
        if (run.Width <= referenceDigitWidth * 0.72)
        {
            digit = 1;
            return true;
        }

        var observed = BuildRaidTimerGlyphTemplate(
            source,
            cropLeft,
            cropTop,
            run,
            digitTop,
            digitBottom,
            redOnly);
        var bestDigit = -1;
        var secondDigit = -1;
        var bestDistance = int.MaxValue;
        var secondDistance = int.MaxValue;
        foreach (var candidate in RaidTimerDigitTemplates)
        {
            var distance = MeasureRaidTimerTemplateDistance(observed, candidate.Pixels);
            if (candidate.Digit == 0 && sameImageZeroTemplate is not null)
            {
                distance = Math.Min(
                    distance,
                    MeasureRaidTimerTemplateDistance(observed, sameImageZeroTemplate));
            }

            if (distance < bestDistance)
            {
                secondDistance = bestDistance;
                secondDigit = bestDigit;
                bestDistance = distance;
                bestDigit = candidate.Digit;
            }
            else if (distance < secondDistance)
            {
                secondDistance = distance;
                secondDigit = candidate.Digit;
            }
        }

        // Une lecture incertaine est abandonnée au profit de Windows OCR. Le
        // seuil absolu rejette un glyphe étranger; la marge empêche notamment
        // un 5 bruité d'être accepté comme 8 ou 9 dans un chrono plausible.
        var maximumDistance = redOnly ? 24 : RaidTimerMaximumTemplateDistance;
        var minimumMargin = redOnly ? 2 : RaidTimerMinimumTemplateMargin;
        var resolvedRedNine = redOnly
            && bestDigit == 8
            && secondDigit == 9
            && secondDistance - bestDistance <= 2
            && LooksLikeRaidTimerNine(observed);
        if (resolvedRedNine)
        {
            bestDigit = 9;
            bestDistance = secondDistance;
        }

        if (bestDistance > maximumDistance
            || (!resolvedRedNine && secondDistance - bestDistance < minimumMargin))
        {
            return false;
        }
        digit = bestDigit;
        return true;
    }

    internal static bool LooksLikeRaidTimerNine(string observed)
    {
        if (observed.Length != RaidTimerTemplateWidth * RaidTimerTemplateHeight)
        {
            return false;
        }

        var lowerLeftInk = 0;
        for (var y = 6; y <= 8; y++)
        {
            for (var x = 0; x <= 2; x++)
            {
                if (observed[(y * RaidTimerTemplateWidth) + x] == '1')
                {
                    lowerLeftInk++;
                }
            }
        }
        return lowerLeftInk <= 3;
    }

    private static string BuildRaidTimerGlyphTemplate(
        System.Drawing.Bitmap source,
        int cropLeft,
        int cropTop,
        TimerGlyphRun run,
        int digitTop,
        int digitBottom,
        bool redOnly)
    {
        var digitHeight = digitBottom - digitTop + 1;
        Span<char> pixels = stackalloc char[RaidTimerTemplateWidth * RaidTimerTemplateHeight];
        var pixelIndex = 0;
        for (var templateY = 0; templateY < RaidTimerTemplateHeight; templateY++)
        {
            var localTop = digitTop
                + (int)Math.Floor(digitHeight * templateY / (double)RaidTimerTemplateHeight);
            var localBottom = digitTop
                + (int)Math.Ceiling(digitHeight * (templateY + 1) / (double)RaidTimerTemplateHeight)
                - 1;
            for (var templateX = 0; templateX < RaidTimerTemplateWidth; templateX++)
            {
                var localLeft = run.Left
                    + (int)Math.Floor(run.Width * templateX / (double)RaidTimerTemplateWidth);
                var localRight = run.Left
                    + (int)Math.Ceiling(run.Width * (templateX + 1) / (double)RaidTimerTemplateWidth)
                    - 1;
                var ink = 0;
                var total = 0;
                for (var localY = localTop; localY <= localBottom; localY++)
                {
                    for (var localX = localLeft; localX <= localRight; localX++)
                    {
                        if (IsRaidTimerInk(
                            source.GetPixel(cropLeft + localX, cropTop + localY),
                            redOnly))
                        {
                            ink++;
                        }
                        total++;
                    }
                }
                pixels[pixelIndex++] = ink * 100 >= total * 38 ? '1' : '0';
            }
        }

        return new string(pixels);
    }

    private static int MeasureRaidTimerTemplateDistance(string observed, string expected)
    {
        var distance = 0;
        for (var index = 0; index < observed.Length; index++)
        {
            if (observed[index] != expected[index])
            {
                distance++;
            }
        }
        return distance;
    }

    private static bool TryGetTimerDigitBand(
        System.Drawing.Bitmap source,
        int cropLeft,
        int cropTop,
        IReadOnlyList<TimerGlyphRun> digitRuns,
        int cropHeight,
        int referenceDigitWidth,
        bool redOnly,
        out int digitTop,
        out int digitBottom)
    {
        digitTop = -1;
        digitBottom = -1;
        var minimumRowInk = Math.Max(3, digitRuns.Sum(run => run.Width) / 24);
        var currentTop = -1;
        var currentInk = 0;
        var bestInk = -1;
        for (var localY = 0; localY <= cropHeight; localY++)
        {
            var rowInk = 0;
            if (localY < cropHeight)
            {
                foreach (var run in digitRuns)
                {
                    for (var localX = run.Left; localX <= run.Right; localX++)
                    {
                        if (IsRaidTimerInk(
                            source.GetPixel(cropLeft + localX, cropTop + localY),
                            redOnly))
                        {
                            rowInk++;
                        }
                    }
                }
            }

            if (rowInk >= minimumRowInk)
            {
                currentTop = currentTop < 0 ? localY : currentTop;
                currentInk += rowInk;
                continue;
            }

            if (currentTop < 0)
            {
                continue;
            }

            var currentBottom = localY - 1;
            var currentHeight = currentBottom - currentTop + 1;
            var bestHeight = digitBottom - digitTop + 1;
            if (currentHeight > bestHeight
                || (currentHeight == bestHeight && currentInk > bestInk))
            {
                digitTop = currentTop;
                digitBottom = currentBottom;
                bestInk = currentInk;
            }
            currentTop = -1;
            currentInk = 0;
        }

        var detectedHeight = digitBottom - digitTop + 1;
        return detectedHeight >= Math.Max(5, referenceDigitWidth)
            && detectedHeight <= referenceDigitWidth * 2.0;
    }

    private static bool TryGetTimerInkBounds(
        System.Drawing.Bitmap source,
        int cropLeft,
        int cropTop,
        TimerGlyphRun run,
        int searchTop,
        int searchBottom,
        bool redOnly,
        out int firstRow,
        out int lastRow)
    {
        firstRow = int.MaxValue;
        lastRow = -1;
        for (var localX = run.Left; localX <= run.Right; localX++)
        {
            for (var localY = searchTop; localY <= searchBottom; localY++)
            {
                if (!IsRaidTimerInk(
                    source.GetPixel(cropLeft + localX, cropTop + localY),
                    redOnly))
                {
                    continue;
                }
                firstRow = Math.Min(firstRow, localY);
                lastRow = Math.Max(lastRow, localY);
            }
        }
        return lastRow >= firstRow;
    }

    private static bool IsRaidTimerInk(System.Drawing.Color color, bool redOnly)
    {
        if (redOnly)
        {
            return color.R >= 95
                && color.R * 2 >= color.G * 3
                && color.R * 2 >= color.B * 3;
        }

        return ((color.R * 54) + (color.G * 183) + (color.B * 19)) >> 8 >= 90;
    }

    internal static bool TrySelectRaidTimerRunWidths(
        IReadOnlyList<int> rawWidths,
        out int selectedStart)
    {
        selectedStart = -1;
        if (rawWidths.Count < 7 || rawWidths.Count > 16)
        {
            return false;
        }

        for (var start = 0; start <= rawWidths.Count - 7; start++)
        {
            var referenceWidth = rawWidths[start];
            if (referenceWidth < 5
                || rawWidths[start + 1] < 1 || rawWidths[start + 1] > referenceWidth * 0.60
                || rawWidths[start + 4] < 1 || rawWidths[start + 4] > referenceWidth * 0.60)
            {
                continue;
            }

            var validDigits = new[] {
                rawWidths[start + 2], rawWidths[start + 3],
                rawWidths[start + 5], rawWidths[start + 6],
            }.All(width => width >= referenceWidth * 0.20
                && width <= referenceWidth * 1.40);
            if (!validDigits)
            {
                continue;
            }

            if (selectedStart >= 0)
            {
                selectedStart = -1;
                return false;
            }
            selectedStart = start;
        }
        return selectedStart >= 0;
    }

    private readonly record struct TimerGlyphRun(int Left, int Right)
    {
        internal int Width => Right - Left + 1;
    }

    private static System.Drawing.Bitmap RenderRaidTimerVariant(
        System.Drawing.Bitmap source,
        BitmapBounds bounds,
        double requestedScale,
        bool monochrome,
        bool redMask = false)
    {
        var maximumDimension = Math.Max(1, OcrEngine.MaxImageDimension);
        var scale = Math.Min(
            requestedScale,
            Math.Min(
                maximumDimension / (double)bounds.Width,
                maximumDimension / (double)bounds.Height));
        var scaledWidth = (int)Math.Max(1, Math.Floor(bounds.Width * scale));
        var scaledHeight = (int)Math.Max(1, Math.Floor(bounds.Height * scale));
        var rendered = new System.Drawing.Bitmap(
            scaledWidth,
            scaledHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(rendered))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new System.Drawing.Rectangle(0, 0, scaledWidth, scaledHeight),
                new System.Drawing.Rectangle(
                    checked((int)bounds.X),
                    checked((int)bounds.Y),
                    checked((int)bounds.Width),
                    checked((int)bounds.Height)),
                System.Drawing.GraphicsUnit.Pixel);
        }

        if (redMask)
        {
            ApplyRaidTimerRedMask(rendered);
        }
        else if (monochrome)
        {
            ApplyRaidTimerMonochromeMask(rendered);
        }
        return rendered;
    }

    private static void ApplyRaidTimerMonochromeMask(System.Drawing.Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(bitmapData.Stride);
            var pixels = new byte[checked(stride * bitmap.Height)];
            Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = bitmapData.Stride >= 0
                    ? y * stride
                    : (bitmap.Height - 1 - y) * stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = row + (x * 4);
                    var luminance = ((pixels[offset + 2] * 54)
                        + (pixels[offset + 1] * 183)
                        + (pixels[offset] * 19)) >> 8;
                    var value = luminance >= 72 ? (byte)0 : (byte)255;
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                    pixels[offset + 3] = 255;
                }
            }
            Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static void ApplyRaidTimerRedMask(System.Drawing.Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(bitmapData.Stride);
            var pixels = new byte[checked(stride * bitmap.Height)];
            Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = bitmapData.Stride >= 0
                    ? y * stride
                    : (bitmap.Height - 1 - y) * stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = row + (x * 4);
                    var blue = pixels[offset];
                    var green = pixels[offset + 1];
                    var red = pixels[offset + 2];
                    var isTimerRed = red >= 75
                        && red * 2 >= green * 3
                        && red * 2 >= blue * 3;
                    var value = isTimerRed ? (byte)0 : (byte)255;
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                    pixels[offset + 3] = 255;
                }
            }
            Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(
        System.Drawing.Bitmap bitmap)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
        encoded.Position = 0;
        using var randomAccessStream = encoded.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
    }

    internal static IReadOnlyList<string> MergeRowFragments(
        IEnumerable<OcrTextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        var rows = new List<List<OcrTextFragment>>();
        foreach (var fragment in fragments
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment.Text))
            .OrderBy(fragment => fragment.Y + (fragment.Height / 2.0))
            .ThenBy(fragment => fragment.X))
        {
            var center = fragment.Y + (fragment.Height / 2.0);
            var row = rows.FirstOrDefault(candidate =>
            {
                var rowCenter = candidate.Average(item => item.Y + (item.Height / 2.0));
                var rowHeight = candidate.Average(item => item.Height);
                var tolerance = Math.Max(3.0, Math.Max(rowHeight, fragment.Height) * 0.55);
                return Math.Abs(center - rowCenter) <= tolerance;
            });
            if (row is null)
            {
                row = [];
                rows.Add(row);
            }
            row.Add(fragment);
        }

        return rows
            .OrderBy(row => row.Average(item => item.Y + (item.Height / 2.0)))
            .Select(row => string.Join(
                " ",
                row.OrderBy(item => item.X)
                    .Select(item => item.Text.Trim())
                    .Where(text => text.Length > 0)))
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal static BitmapBounds CalculateExtractionPanelBounds(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                width == 0 ? nameof(width) : nameof(height));
        }

        var cropX = (uint)Math.Floor(width * 0.68);
        var cropHeight = (uint)Math.Max(1, Math.Floor(height * 0.65));
        return new BitmapBounds
        {
            X = cropX,
            Y = 0,
            Width = Math.Max(1u, width - cropX),
            Height = Math.Min(height, cropHeight),
        };
    }

    internal static BitmapBounds CalculateQuestPanelBounds(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                width == 0 ? nameof(width) : nameof(height));
        }

        // L'onglet Tâches occupe la partie gauche et centrale. Windows OCR
        // limite la plus grande dimension d'une image; 2500 px garde la zone
        // utile d'un écran ultra-large sans redimensionnement destructeur.
        return new BitmapBounds
        {
            X = 0,
            Y = 0,
            Width = Math.Min(width, 2500u),
            Height = Math.Min(height, 2500u),
        };
    }

    internal static BitmapBounds CalculateRaidTimerBounds(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                width == 0 ? nameof(width) : nameof(height));
        }

        // Le temps restant occupe la petite cellule à l'extrême droite de
        // l'entête du panneau O. Ce cadrage dédié évite que le moteur retenu
        // pour les noms EXFIL perde les chiffres beaucoup plus petits.
        var cropX = (uint)Math.Floor(width * 0.945);
        var cropHeight = (uint)Math.Max(1, Math.Floor(height * 0.065));
        return new BitmapBounds
        {
            X = cropX,
            Y = 0,
            Width = Math.Max(1u, width - cropX),
            Height = Math.Min(height, cropHeight),
        };
    }

    private static BitmapBounds CalculateRaidTimerTightBounds(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(
                width == 0 ? nameof(width) : nameof(height));
        }

        return new BitmapBounds
        {
            X = (uint)Math.Floor(width * 0.945),
            Y = 0,
            Width = Math.Max(1u, width - (uint)Math.Floor(width * 0.945)),
            Height = Math.Min(height, (uint)Math.Max(1, Math.Floor(height * 0.05))),
        };
    }

    private static BitmapBounds CalculateRaidTimerHeaderBounds(uint width, uint height)
    {
        var cropX = (uint)Math.Floor(width * 0.74);
        var cropHeight = (uint)Math.Max(1, Math.Floor(height * 0.065));
        return new BitmapBounds
        {
            X = cropX,
            Y = 0,
            Width = Math.Max(1u, width - cropX),
            Height = Math.Min(height, cropHeight),
        };
    }

    private static List<OcrEngine> CreateEngines()
    {
        var engines = new List<OcrEngine>();
        var usedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var preferred = OcrEngine.TryCreateFromUserProfileLanguages();
        AddEngine(preferred, engines, usedLanguages);

        var available = OcrEngine.AvailableRecognizerLanguages;
        var cultureName = CultureInfo.CurrentUICulture.Name;
        var cultureLanguage = available.FirstOrDefault(language =>
            string.Equals(language.LanguageTag, cultureName, StringComparison.OrdinalIgnoreCase));
        AddEngine(
            cultureLanguage is null ? null : OcrEngine.TryCreateFromLanguage(cultureLanguage),
            engines,
            usedLanguages);

        var english = available.FirstOrDefault(language =>
            language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        AddEngine(
            english is null ? null : OcrEngine.TryCreateFromLanguage(english),
            engines,
            usedLanguages);
        return engines;
    }

    private static void AddEngine(
        OcrEngine? engine,
        ICollection<OcrEngine> engines,
        ISet<string> usedLanguages)
    {
        if (engine is not null && usedLanguages.Add(engine.RecognizerLanguage.LanguageTag))
        {
            engines.Add(engine);
        }
    }

    private static async Task WaitForCompleteFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        long previousLength = -1;
        DateTime previousWriteTimeUtc = DateTime.MinValue;
        var stableProbes = 0;
        for (var probe = 0; probe < ReadyProbeCount; probe++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(filePath);
                if (info.Exists
                    && info.Length > 0
                    && info.Length == previousLength
                    && info.LastWriteTimeUtc == previousWriteTimeUtc)
                {
                    stableProbes++;
                    if (stableProbes >= RequiredStableProbes)
                    {
                        using var stream = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        if (stream.Length == info.Length)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    stableProbes = 0;
                }
                previousLength = info.Exists ? info.Length : -1;
                previousWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException)
            {
                previousLength = -1;
                previousWriteTimeUtc = DateTime.MinValue;
                stableProbes = 0;
            }

            await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException("La capture Tarkov n'est pas encore prête.");
    }
}