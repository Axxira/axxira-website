using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class AutomaticReanchorRequestTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("capture auto accepte un nouveau fichier du raid", AcceptsNewMatchingFile, failures);
        Run("capture auto refuse un ancien fichier", RejectsExistingFile, failures);
        Run("capture auto refuse un fichier antidaté", RejectsOldTimestamp, failures);
        Run("capture auto refuse un autre raid", RejectsDifferentRaid, failures);
        Run("capture auto accepte une notification tardive du bon fichier", AcceptsDelayedNotification, failures);
        Run("capture auto refuse un F8 manuel tardif", RejectsLateManualCandidate, failures);
        Run("capture auto devient ambiguë avec deux fichiers", DetectsAmbiguousCandidates, failures);
    }

    private static void AcceptsNewMatchingFile()
    {
        var request = NewRequest();
        Require(request.TryRegisterCandidate(
            "new.png",
            request.RequestedAtUtc + TimeSpan.FromMilliseconds(20),
            raidGeneration: 7,
            nowTimestamp: request.RequestedAtTimestamp + Stopwatch.Frequency),
            "la nouvelle capture devrait être associée");
        Require(request.CandidateFileName == "new.png", "le candidat associé est incorrect");
    }

    private static void RejectsExistingFile()
    {
        var request = NewRequest();
        Require(!request.TryRegisterCandidate(
            "old.png",
            request.RequestedAtUtc,
            7,
            request.RequestedAtTimestamp),
            "un fichier inventorié avant F8 ne doit pas être associé");
    }

    private static void RejectsOldTimestamp()
    {
        var request = NewRequest();
        Require(!request.TryRegisterCandidate(
            "new.png",
            request.RequestedAtUtc - TimeSpan.FromSeconds(2),
            7,
            request.RequestedAtTimestamp),
            "une ancienne capture ne doit pas être associée");
    }

    private static void RejectsDifferentRaid()
    {
        var request = NewRequest();
        Require(!request.TryRegisterCandidate(
            "new.png",
            request.RequestedAtUtc,
            8,
            request.RequestedAtTimestamp),
            "une capture d'un autre raid doit être refusée");
    }

    private static void DetectsAmbiguousCandidates()
    {
        var request = NewRequest();
        Require(request.TryRegisterCandidate("one.png", request.RequestedAtUtc, 7, request.RequestedAtTimestamp), "premier candidat refusé");
        Require(request.TryRegisterCandidate("two.png", request.RequestedAtUtc, 7, request.RequestedAtTimestamp), "second candidat refusé");
        Require(request.IsAmbiguous, "deux nouveaux fichiers doivent empêcher une suppression automatique");
    }

    private static void AcceptsDelayedNotification()
    {
        var request = NewRequest();
        Require(request.TryRegisterCandidate(
            "delayed.png",
            request.RequestedAtUtc + TimeSpan.FromMilliseconds(120),
            7,
            request.RequestedAtTimestamp + (3L * Stopwatch.Frequency)),
            "une notification tardive devrait être acceptée si le fichier a été créé juste après F8");
    }

    private static void RejectsLateManualCandidate()
    {
        var request = NewRequest();
        Require(!request.TryRegisterCandidate(
            "manual.png",
            request.RequestedAtUtc + TimeSpan.FromSeconds(3),
            7,
            request.RequestedAtTimestamp + (3L * Stopwatch.Frequency)),
            "un F8 manuel tardif ne doit pas être classé comme capture automatique");
    }

    private static AutomaticReanchorRequest NewRequest()
    {
        return new AutomaticReanchorRequest(
            requestedAtTimestamp: Stopwatch.GetTimestamp(),
            requestedAtUtc: DateTimeOffset.UtcNow,
            raidGeneration: 7,
            existingFileNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "old.png" });
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
            failures.Add($"{name} — {exception.Message}");
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
