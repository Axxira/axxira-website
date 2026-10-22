using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class ScreenshotFolderCleanerTests
{
    internal const int TestCount = 10;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("Documents vide, relatif ou racine refusé", RejectsUnsafeDocumentsRoots, failures);
        Run("nettoyage borné aux PNG du dossier EFT exact", DeletesOnlyTopLevelPngFiles, failures);
        Run("jonction parent refusée par attribut de handle", RejectsParentJunctionAttributes, failures);
        Run("identité remplacée préservée", PreservesReplacedFileIdentity, failures);
        Run("remplacement du dossier détecté sans suppression", PreservesDirectoryReplacement, failures);
        Run("nouvelle capture hors plan préservée", NeverReenumeratesForDeletion, failures);
        Run("verrou transitoire repris de façon bornée", RetriesTransientLock, failures);
        Run("nettoyage exécuté une seule fois au démarrage", WatcherRunsCleanupOnlyOnce, failures);
        Run("exception de nettoyage débloque le rattrapage", CleanupExceptionUnblocksCatchUp, failures);
        Run("Start immédiat et Created actif pendant nettoyage", StartReturnsFastAndObservesNewScreenshot, failures);
    }

    private static void RejectsUnsafeDocumentsRoots()
    {
        Require(!ScreenshotFolderCleaner.TryResolveScreenshotsPath(null, out _),
            "Documents null a été accepté");
        Require(!ScreenshotFolderCleaner.TryResolveScreenshotsPath("  ", out _),
            "Documents vide a été accepté");
        Require(!ScreenshotFolderCleaner.TryResolveScreenshotsPath("Documents", out _),
            "Documents relatif a été accepté");

        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Require(!ScreenshotFolderCleaner.TryResolveScreenshotsPath(root, out _),
            "une racine de volume a été acceptée comme Documents");

        var documents = Path.Combine(Path.GetTempPath(), $"RaidPilot-docs-{Guid.NewGuid():N}");
        Require(ScreenshotFolderCleaner.TryResolveScreenshotsPath(documents, out var screenshots),
            "un chemin Documents absolu normal a été refusé");
        Require(string.Equals(
                screenshots,
                Path.Combine(documents, "Escape from Tarkov", "Screenshots"),
                StringComparison.OrdinalIgnoreCase),
            $"cible dérivée inattendue : {screenshots}");
    }

    private static void DeletesOnlyTopLevelPngFiles()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        try
        {
            var oldPng = Path.Combine(screenshotsPath, "ancienne.png");
            var recentPng = Path.Combine(screenshotsPath, "raid-qui-demarre.png");
            var unrelated = Path.Combine(screenshotsPath, "notes.txt");
            var nestedDirectory = Path.Combine(screenshotsPath, "archive");
            var nestedPng = Path.Combine(nestedDirectory, "imbriquee.png");
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(oldPng, "old");
            File.WriteAllText(recentPng, "live");
            File.WriteAllText(unrelated, "keep");
            File.WriteAllText(nestedPng, "keep");
            SetOldTimestamp(oldPng);

            var result = ScreenshotFolderCleaner.ExecuteForDocumentsRoot(
                documents,
                DateTimeOffset.UtcNow,
                maximumAttempts: 1);

            Require(!File.Exists(oldPng), "l'ancien PNG de premier niveau a été conservé");
            Require(File.Exists(recentPng), "un PNG créé près du démarrage a été supprimé");
            Require(File.Exists(unrelated), "un fichier non PNG a été supprimé");
            Require(File.Exists(nestedPng), "un sous-dossier a été parcouru à tort");
            Require(result.CandidateCount == 1
                    && result.DeletedCount == 1
                    && result.SkippedNewerCount == 1
                    && result.FailedCount == 0,
                $"résultat inattendu : {result}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RejectsParentJunctionAttributes()
    {
        Require(ScreenshotFolderCleaner.IsSafeCleanupDirectoryAttributes(FileAttributes.Directory),
            "un dossier normal a été refusé");
        Require(!ScreenshotFolderCleaner.IsSafeCleanupDirectoryAttributes(
                FileAttributes.Directory | FileAttributes.ReparsePoint),
            "une jonction de dossier parent a été acceptée");
        Require(!ScreenshotFolderCleaner.IsSafeCleanupDirectoryAttributes(FileAttributes.Normal),
            "un fichier a été accepté comme dossier protégé");
    }

    private static void PreservesReplacedFileIdentity()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        FileStream? originalIdentityHandle = null;
        try
        {
            var path = Path.Combine(screenshotsPath, "candidate.png");
            File.WriteAllText(path, "same-size");
            var timestamp = DateTime.UtcNow.AddMinutes(-5);
            SetTimestamp(path, timestamp);

            var hooks = new ScreenshotCleanupTestHooks
            {
                AfterCandidatesCaptured = _ =>
                {
                    try
                    {
                        // Keep the unlinked file object alive so NTFS cannot
                        // recycle its file ID for the replacement.
                        originalIdentityHandle = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        File.Delete(path);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException($"suppression identité: {exception.Message}", exception);
                    }
                    try
                    {
                        File.WriteAllText(path, "same-size");
                        SetTimestamp(path, timestamp);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException($"création identité: {exception.Message}", exception);
                    }
                },
            };

            var result = ScreenshotFolderCleaner.ExecuteForDocumentsRoot(
                documents,
                DateTimeOffset.UtcNow,
                maximumAttempts: 1,
                hooks);

            Require(File.Exists(path), "le nouveau fichier portant le même nom a été supprimé");
            Require(result.DeletedCount == 0 && result.ChangedIdentityCount == 1,
                $"le remplacement d'identité n'a pas été détecté : {result}");
        }
        finally
        {
            originalIdentityHandle?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void PreservesDirectoryReplacement()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        try
        {
            var oldPng = Path.Combine(screenshotsPath, "ancienne.png");
            File.WriteAllText(oldPng, "old");
            SetOldTimestamp(oldPng);
            var replacementCreated = false;
            var hooks = new ScreenshotCleanupTestHooks
            {
                AfterCandidatesCaptured = _ =>
                {
                    File.Delete(oldPng);
                    Directory.Delete(screenshotsPath);
                    Directory.CreateDirectory(screenshotsPath);
                    File.WriteAllText(Path.Combine(screenshotsPath, "ancienne.png"), "replacement");
                    replacementCreated = true;
                },
            };

            var result = ScreenshotFolderCleaner.ExecuteForDocumentsRoot(
                documents,
                DateTimeOffset.UtcNow,
                maximumAttempts: 1,
                hooks);

            Require(replacementCreated, "le scénario de remplacement n'a pas été exécuté");
            Require(File.Exists(Path.Combine(screenshotsPath, "ancienne.png")),
                "le fichier du dossier remplaçant a été supprimé");
            Require(result.DeletedCount == 0 && result.ChangedIdentityCount == 1,
                $"le remplacement de dossier n'a pas été détecté : {result}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void NeverReenumeratesForDeletion()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        try
        {
            var oldPng = Path.Combine(screenshotsPath, "ancienne.png");
            var livePng = Path.Combine(screenshotsPath, "nouvelle.png");
            File.WriteAllText(oldPng, "old");
            SetOldTimestamp(oldPng);
            var hooks = new ScreenshotCleanupTestHooks
            {
                AfterCandidatesCaptured = _ =>
                {
                    File.WriteAllText(livePng, "live");
                    // Even an intentionally backdated newcomer must remain:
                    // deletion is limited to captured startup identities.
                    SetOldTimestamp(livePng);
                },
            };

            var result = ScreenshotFolderCleaner.ExecuteForDocumentsRoot(
                documents,
                DateTimeOffset.UtcNow,
                maximumAttempts: 1,
                hooks);

            Require(!File.Exists(oldPng), "le candidat initial n'a pas été supprimé");
            Require(File.Exists(livePng), "une capture créée après le plan a été supprimée");
            Require(result.CandidateCount == 1 && result.DeletedCount == 1,
                $"la liste destructive n'est pas restée figée : {result}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RetriesTransientLock()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        FileStream? exclusiveLock = null;
        try
        {
            var oldPng = Path.Combine(screenshotsPath, "verrouillee.png");
            File.WriteAllText(oldPng, "old");
            SetOldTimestamp(oldPng);
            var hooks = new ScreenshotCleanupTestHooks
            {
                AfterCandidatesCaptured = _ =>
                    exclusiveLock = new FileStream(
                        oldPng,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None),
                BeforeDeleteAttempt = (_, attempt) =>
                {
                    if (attempt == 2)
                    {
                        exclusiveLock?.Dispose();
                        exclusiveLock = null;
                    }
                },
                RetryDelay = _ => TimeSpan.Zero,
            };

            var result = ScreenshotFolderCleaner.ExecuteForDocumentsRoot(
                documents,
                DateTimeOffset.UtcNow,
                maximumAttempts: 3,
                hooks);

            Require(!File.Exists(oldPng), "le verrou transitoire n'a pas été repris");
            Require(result.DeletedCount == 1
                    && result.RetryCount == 1
                    && result.FailedCount == 0,
                $"retries bornés inattendus : {result}");
        }
        finally
        {
            exclusiveLock?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WatcherRunsCleanupOnlyOnce()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var firstOldPng = Path.Combine(screenshotsPath, "premier.png");
            File.WriteAllText(firstOldPng, "old");
            SetOldTimestamp(firstOldPng);

            using var watcher = ScreenshotFolderWatcher.CreateWithStartupCleanupForTesting(
                documents,
                TimeSpan.FromMilliseconds(25),
                () => now);
            watcher.Start();
            Require(SpinWait.SpinUntil(
                    () => watcher.LastStartupCleanupResult is not null,
                    TimeSpan.FromSeconds(2)),
                "le nettoyage asynchrone n'a pas terminé");
            Require(!File.Exists(firstOldPng), "le nettoyage initial n'a pas supprimé le PNG");

            watcher.Stop();
            var laterPng = Path.Combine(screenshotsPath, "raid-en-cours.png");
            File.WriteAllText(laterPng, "live");
            SetOldTimestamp(laterPng);
            watcher.Start();
            Thread.Sleep(150);

            Require(File.Exists(laterPng),
                "un redémarrage interne du watcher a relancé le nettoyage de processus");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CleanupExceptionUnblocksCatchUp()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        try
        {
            var now = DateTimeOffset.UtcNow;
            const string fileName = "2026-10-19[12-00]_1, 2, 3_0, 0, 0, 1_0 (0).png";
            var path = Path.Combine(screenshotsPath, fileName);
            File.WriteAllText(path, string.Empty);
            SetTimestamp(path, now.UtcDateTime);
            using var observed = new ManualResetEventSlim();
            var observationCount = 0;
            using var watcher = ScreenshotFolderWatcher.CreateWithStartupCleanupForTesting(
                documents,
                TimeSpan.FromMilliseconds(25),
                () => now,
                _ => throw new IOException("échec simulé"));
            watcher.ObservationCreated += (_, observation) =>
            {
                if (observation.FileName == fileName)
                {
                    Interlocked.Increment(ref observationCount);
                    observed.Set();
                }
            };

            watcher.Start();

            Require(observed.Wait(TimeSpan.FromSeconds(2)),
                "l'exception a laissé le rattrapage bloqué");
            Require(watcher.LastStartupCleanupResult?.FailedCount == 1,
                "l'échec fatal n'a pas été rapporté");
            Thread.Sleep(100);
            Require(Volatile.Read(ref observationCount) == 1,
                "la completion/rattrapage n'est pas idempotent");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void StartReturnsFastAndObservesNewScreenshot()
    {
        var documents = CreateScreenshotsDirectory(out var root, out var screenshotsPath);
        using var cleanupStarted = new ManualResetEventSlim();
        using var releaseCleanup = new ManualResetEventSlim();
        using var observed = new ManualResetEventSlim();
        try
        {
            var now = DateTimeOffset.UtcNow;
            const string fileName = "2026-10-19[12-01]_4, 5, 6_0, 0, 0, 1_0 (0).png";
            using var watcher = ScreenshotFolderWatcher.CreateWithStartupCleanupForTesting(
                documents,
                TimeSpan.FromMilliseconds(25),
                () => now,
                _ =>
                {
                    cleanupStarted.Set();
                    releaseCleanup.Wait(TimeSpan.FromSeconds(3));
                    return ScreenshotCleanupResult.Empty;
                });
            watcher.ObservationCreated += (_, observation) =>
            {
                if (observation.FileName == fileName)
                {
                    observed.Set();
                }
            };

            var stopwatch = Stopwatch.StartNew();
            watcher.Start();
            stopwatch.Stop();

            Require(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"Start a bloqué {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
            Require(cleanupStarted.Wait(TimeSpan.FromSeconds(1)),
                "le worker de nettoyage n'a pas démarré");
            File.WriteAllText(Path.Combine(screenshotsPath, fileName), string.Empty);
            Require(observed.Wait(TimeSpan.FromSeconds(2)),
                "Created n'était pas actif pendant le nettoyage");
        }
        finally
        {
            releaseCleanup.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateScreenshotsDirectory(
        out string root,
        out string screenshotsPath)
    {
        root = Path.Combine(Path.GetTempPath(), $"RaidPilot-cleanup-{Guid.NewGuid():N}");
        var documents = Path.Combine(root, "Documents");
        screenshotsPath = Path.Combine(documents, "Escape from Tarkov", "Screenshots");
        Directory.CreateDirectory(screenshotsPath);
        return documents;
    }

    private static void SetOldTimestamp(string path)
    {
        SetTimestamp(path, DateTime.UtcNow.AddMinutes(-5));
    }

    private static void SetTimestamp(string path, DateTime timestampUtc)
    {
        File.SetCreationTimeUtc(path, timestampUtc);
        File.SetLastWriteTimeUtc(path, timestampUtc);
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
