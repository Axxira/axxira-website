namespace RaidPilot.Overlay.Tests;

internal static class RaidTimerTests
{
    internal const int TestCount = 15;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("chrono OCR lit MM:SS", ParsesMinutesAndSeconds, failures);
        Run("chrono OCR corrige les glyphes sûrs", ParsesSafeOcrConfusions, failures);
        Run("chrono OCR récupère une corruption compacte réelle", ParsesCompactRealOcrConfusion, failures);
        Run("chrono OCR compact refuse un faux texte", RejectsCompactFalsePositive, failures);
        Run("chrono pixels sélectionne une seule fenêtre H:MM:SS", SelectsUniqueTimerGlyphWindow, failures);
        Run("chrono pixels refuse deux fenêtres plausibles", RejectsAmbiguousTimerGlyphWindows, failures);
        Run("chrono rouge distingue 9 de 8 par le bas gauche", DistinguishesRedNineFromEight, failures);
        Run("chrono OCR préfère la ligne étiquetée", PrefersLabelledTimer, failures);
        Run("chrono OCR refuse les valeurs absurdes", RejectsInvalidValues, failures);
        Run("chrono retire le délai de lecture", CompensatesRecognitionDelay, failures);
        Run("chrono refuse génération et capture anciennes", RejectsStaleUpdates, failures);
        Run("chrono accepte une correction cohérente", AcceptsConsistentCorrection, failures);
        Run("chrono confirme une grande discontinuité", ConfirmsLargeDiscontinuity, failures);
        Run("nouveau raid attend une mesure exacte", NewSessionStartsUnsynchronized, failures);
        Run("chrono optionnel ne bloque pas les extractions", OptionalTimerDoesNotBlockExtraction, failures);
    }

    private static void ParsesMinutesAndSeconds()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["EXFIL", "RAID TIME 39:42", "Crossroads"],
            out var seconds);
        Require(parsed && seconds == (39 * 60) + 42, $"temps inattendu: {seconds}");
    }

    private static void ParsesSafeOcrConfusions()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["TEMPS I:O2:3S"],
            out var seconds);
        Require(parsed && seconds == 3755, $"confusion OCR mal corrigée: {seconds}");
    }

    private static void ParsesCompactRealOcrConfusion()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["oe36:sø"],
            out var seconds);
        Require(parsed && seconds == (36 * 60) + 50,
            $"corruption compacte réelle mal récupérée: {seconds}");
    }

    private static void RejectsCompactFalsePositive()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["se36:sø", "Trouvez le point d'extraction", "EXFIL01"],
            out _);
        Require(!parsed, "un faux texte compact a été accepté comme chrono");
    }

    private static void SelectsUniqueTimerGlyphWindow()
    {
        Require(
            WindowsScreenshotTextReader.TrySelectRaidTimerRunWidths(
                [10, 2, 9, 8, 2, 10, 9], out var start) && start == 0,
            "la fenêtre H:MM:SS unique n'a pas été sélectionnée");
    }

    private static void RejectsAmbiguousTimerGlyphWindows()
    {
        Require(
            !WindowsScreenshotTextReader.TrySelectRaidTimerRunWidths(
                [10, 2, 9, 8, 2, 10, 9, 30, 10, 2, 9, 8, 2, 10, 9], out _),
            "deux fenêtres plausibles ne doivent jamais être départagées arbitrairement");
    }

    private static void DistinguishesRedNineFromEight()
    {
        var nineLike = new string('0', 8 * 12).ToCharArray();
        Require(WindowsScreenshotTextReader.LooksLikeRaidTimerNine(new string(nineLike)),
            "un bas gauche vide devrait rester compatible avec 9");
        for (var y = 6; y <= 8; y++)
        for (var x = 0; x <= 2; x++)
            nineLike[(y * 8) + x] = '1';
        Require(!WindowsScreenshotTextReader.LooksLikeRaidTimerNine(new string(nineLike)),
            "un bas gauche fortement rempli doit rester compatible avec 8, pas 9");
    }

    private static void PrefersLabelledTimer()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["05:00", "RAID TIME 41:10"],
            out var seconds);
        Require(parsed && seconds == 2470, $"mauvais candidat choisi: {seconds}");
    }

    private static void RejectsInvalidValues()
    {
        var parsed = RaidTimerParser.TryParseRemainingSeconds(
            ["RAID TIME 12:79", "TIME 3:30:00", "RAID TIME 89:42", "ZB-1011"],
            out _);
        Require(!parsed, "une valeur hors bornes a été acceptée");
    }

    private static void CompensatesRecognitionDelay()
    {
        var countdown = new RaidCountdown(frequency: 1000);
        var generation = countdown.StartSession();
        Require(
            countdown.TrySynchronize(generation, 600, captureTimestamp: 10_000, currentTimestamp: 12_500),
            "la mesure courante est refusée");

        var first = countdown.GetSnapshot(12_500);
        var later = countdown.GetSnapshot(17_500);
        Require(first.IsSynchronized && Math.Abs(first.RemainingSeconds - 597.5) < 0.001,
            $"délai OCR non retiré: {first.RemainingSeconds}");
        Require(Math.Abs(later.RemainingSeconds - 592.5) < 0.001,
            $"décompte monotone incorrect: {later.RemainingSeconds}");
    }

    private static void RejectsStaleUpdates()
    {
        var countdown = new RaidCountdown(frequency: 1000);
        var oldGeneration = countdown.StartSession();
        Require(countdown.TrySynchronize(oldGeneration, 900, 10_000, 10_500),
            "première mesure refusée");

        var currentGeneration = countdown.StartSession();
        Require(!countdown.TrySynchronize(oldGeneration, 850, 11_000, 11_100),
            "ancienne génération acceptée");
        Require(countdown.TrySynchronize(currentGeneration, 800, 20_000, 20_100),
            "mesure de la nouvelle génération refusée");
        Require(!countdown.TrySynchronize(currentGeneration, 810, 19_000, 20_200),
            "capture plus ancienne acceptée");
    }

    private static void AcceptsConsistentCorrection()
    {
        var countdown = new RaidCountdown(frequency: 1000);
        var generation = countdown.StartSession();
        Require(countdown.TrySynchronize(generation, 600, 10_000, 10_000),
            "première mesure refusée");
        Require(countdown.TrySynchronize(generation, 590, 20_000, 20_200),
            "correction cohérente refusée");

        var snapshot = countdown.GetSnapshot(20_200);
        Require(Math.Abs(snapshot.RemainingSeconds - 589.8) < 0.001,
            $"correction cohérente incorrecte: {snapshot.RemainingSeconds}");
    }

    private static void ConfirmsLargeDiscontinuity()
    {
        var countdown = new RaidCountdown(frequency: 1000);
        var generation = countdown.StartSession();
        Require(countdown.TrySynchronize(generation, 600, 10_000, 10_500),
            "première mesure refusée");
        Require(!countdown.TrySynchronize(generation, 3000, 15_000, 15_100),
            "une seule lecture aberrante a remplacé le chrono");

        var protectedSnapshot = countdown.GetSnapshot(15_100);
        Require(Math.Abs(protectedSnapshot.RemainingSeconds - 594.9) < 0.001,
            $"l'ancre saine a été perdue: {protectedSnapshot.RemainingSeconds}");

        Require(countdown.TrySynchronize(generation, 2998, 17_000, 17_100),
            "la seconde lecture cohérente n'a pas confirmé la correction");
        var confirmedSnapshot = countdown.GetSnapshot(17_100);
        Require(Math.Abs(confirmedSnapshot.RemainingSeconds - 2997.9) < 0.001,
            $"correction confirmée incorrecte: {confirmedSnapshot.RemainingSeconds}");
    }

    private static void NewSessionStartsUnsynchronized()
    {
        var countdown = new RaidCountdown(frequency: 1000);
        var generation = countdown.StartSession();
        var snapshot = countdown.GetSnapshot(42_000);
        Require(snapshot.Generation == generation && snapshot.IsActive && !snapshot.IsSynchronized,
            "le nouveau raid ne devrait pas inventer une durée");
    }

    private static void OptionalTimerDoesNotBlockExtraction()
    {
        var stalledTimer = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var extractionLines = new[] { "EXFIL01 Crossroads", "EXFIL02 Trailer Park" };
        var extractionTask = OverlayForm.AwaitRequiredScanAsync(
            Task.FromResult<IReadOnlyList<string>>(extractionLines),
            stalledTimer.Task);

        try
        {
            Require(
                extractionTask.Wait(TimeSpan.FromSeconds(1)),
                "l'OCR du chrono en attente retarde encore la lecture EXFIL");
            Require(
                extractionTask.Result.SequenceEqual(extractionLines),
                "le résultat EXFIL a été altéré par l'enrichissement chrono");
        }
        finally
        {
            stalledTimer.TrySetResult(true);
        }
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine($"OK  {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception.Message}");
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
