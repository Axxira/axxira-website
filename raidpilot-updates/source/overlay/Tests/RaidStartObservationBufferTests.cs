namespace RaidPilot.Overlay.Tests;

internal static class RaidStartObservationBufferTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("capture F8 juste avant le nouveau raid conservée", CarriesRecentObservation, failures);
        Run("capture F8 ancienne rejetée au nouveau raid", RejectsExpiredObservation, failures);
        Run("capture d'une autre génération rejetée", RejectsWrongGeneration, failures);
        Run("capture de départ consommée une seule fois", ConsumesObservationOnce, failures);
        Run("capture sans transition validée une seule fois", CommitsStableSessionObservation, failures);
        Run("validations différées indépendantes", MultipleSequencesCommitIndependently, failures);
        Run("captures de transition rejouées dans l'ordre", TransitionDrainsObservationsInOrder, failures);
    }

    private static void CarriesRecentObservation()
    {
        var buffer = new RaidStartObservationBuffer(
            frequency: 1000,
            carryWindow: TimeSpan.FromSeconds(8));
        var expected = Observation("recent.png", 12.5);
        var writtenAt = DateTimeOffset.Parse("2026-10-19T14:00:00Z");
        buffer.Remember(
            expected,
            writtenAt,
            captureTimestamp: 9_750,
            raidGeneration: 7,
            observedAtTimestamp: 10_000);

        Require(buffer.TryTakeForRaidTransition(7, 17_999, out var carried),
            "la capture récente n'a pas traversé le reset de raid");
        Require(ReferenceEquals(carried.Observation, expected)
            && carried.FileTimestampUtc == writtenAt
            && carried.CaptureTimestamp == 9_750,
            "la capture conservée a été altérée");
    }

    private static void RejectsExpiredObservation()
    {
        var buffer = new RaidStartObservationBuffer(
            frequency: 1000,
            carryWindow: TimeSpan.FromSeconds(8));
        buffer.Remember(Observation("old.png", 1), DateTimeOffset.UtcNow, 900, 3, 1_000);

        Require(!buffer.TryTakeForRaidTransition(3, 9_001, out _),
            "une capture de l'ancien raid a été réutilisée");
    }

    private static void RejectsWrongGeneration()
    {
        var buffer = new RaidStartObservationBuffer(frequency: 1000);
        buffer.Remember(Observation("wrong.png", 1), DateTimeOffset.UtcNow, 900, 4, 1_000);

        Require(!buffer.TryTakeForRaidTransition(5, 1_500, out _),
            "une capture d'une autre génération a été réutilisée");
    }

    private static void ConsumesObservationOnce()
    {
        var buffer = new RaidStartObservationBuffer(frequency: 1000);
        buffer.Remember(Observation("once.png", 1), DateTimeOffset.UtcNow, 900, 2, 1_000);

        Require(buffer.TryTakeForRaidTransition(2, 1_500, out _),
            "la première consommation a échoué");
        Require(!buffer.TryTakeForRaidTransition(2, 1_600, out _),
            "la capture a été rejouée une deuxième fois");
    }

    private static void CommitsStableSessionObservation()
    {
        var buffer = new RaidStartObservationBuffer(frequency: 1000);
        var expected = Observation("stable.png", 4);
        var sequence = buffer.Remember(expected, DateTimeOffset.UtcNow, 900, 2, 1_000);

        Require(sequence != 0
            && buffer.TryTakeForStableSession(sequence, 2, out var committed)
            && ReferenceEquals(committed.Observation, expected),
            "la capture différée n'a pas été validée dans le raid stable");
        Require(!buffer.TryTakeForStableSession(sequence, 2, out _),
            "la capture différée a été validée deux fois");
    }

    private static void MultipleSequencesCommitIndependently()
    {
        var buffer = new RaidStartObservationBuffer(frequency: 1000);
        var first = Observation("first.png", 1);
        var firstSequence = buffer.Remember(
            first,
            DateTimeOffset.UtcNow,
            900,
            3,
            1_000);
        var replacement = Observation("replacement.png", 2);
        var replacementSequence = buffer.Remember(
            replacement,
            DateTimeOffset.UtcNow,
            1_100,
            3,
            1_200);

        Require(buffer.TryTakeForStableSession(firstSequence, 3, out var firstCommitted)
            && ReferenceEquals(firstCommitted.Observation, first),
            "la première capture différée a été perdue");
        Require(buffer.TryTakeForStableSession(replacementSequence, 3, out var committed)
            && ReferenceEquals(committed.Observation, replacement),
            "la deuxième capture différée a été perdue");
    }

    private static void TransitionDrainsObservationsInOrder()
    {
        var buffer = new RaidStartObservationBuffer(frequency: 1000);
        var first = Observation("first.png", 1);
        var second = Observation("second.png", 2);
        buffer.Remember(first, DateTimeOffset.UtcNow, 900, 3, 1_000);
        buffer.Remember(second, DateTimeOffset.UtcNow, 1_100, 3, 1_200);

        Require(buffer.TryTakeForRaidTransition(3, 1_500, out var firstCarried)
            && ReferenceEquals(firstCarried.Observation, first),
            "la première capture de transition n'a pas été rejouée en premier");
        Require(buffer.TryTakeForRaidTransition(3, 1_500, out var secondCarried)
            && ReferenceEquals(secondCarried.Observation, second),
            "la deuxième capture de transition n'a pas été rejouée");
        Require(!buffer.TryTakeForRaidTransition(3, 1_500, out _),
            "une capture de transition a été rejouée plus d'une fois");
    }

    private static ScreenshotObservation Observation(string fileName, double x) =>
        new(fileName, x, 0, 0, 0, 0, 0, 1, 90);

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
