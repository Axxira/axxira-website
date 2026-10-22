using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class VisualKeyframeStoreTests
{
    internal const int TestCount = 5;

    private const int Width = 64;
    private const int Height = 36;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("mémoire visuelle: sauvegarde et recharge un repère", SavesAndReloadsKeyframe, failures);
        Run("mémoire visuelle: snapshot corrompu ne remplace rien", RejectsCorruptedSnapshotAtomically, failures);
        Run("mémoire visuelle: JSON corrompu ne remplace rien", RejectsCorruptedJsonAtomically, failures);
        Run("mémoire visuelle: chemin séparé par joueur", UsesPerPlayerPath, failures);
        Run("mémoire visuelle: sauvegarde atomique sans temporaire", SavesAtomicallyWithoutLeftovers, failures);
    }

    private static void SavesAndReloadsKeyframe()
    {
        InTemp(root =>
        {
            var path = Path.Combine(root, "keyframes.json");
            var source = NewRecognizer();
            var exact = new VisualKeyframePose(14, 2, -33, 87);
            Require(source.AddExactKeyframe(
                "woods",
                Scene(seed: 17),
                Width,
                Height,
                exact,
                Ticks(1),
                out _),
                "précondition: repère non ajouté");
            Require(VisualKeyframeStore.TrySave(source, path),
                "sauvegarde mémoire visuelle refusée");

            var restored = NewRecognizer();
            Require(VisualKeyframeStore.TryLoad(restored, path),
                "rechargement mémoire visuelle refusé");
            Require(restored.KeyframeCountForMap("woods") == 1,
                "repère perdu au rechargement");

            _ = restored.Observe(
                "woods", Scene(seed: 17), Width, Height,
                new VisualKeyframePose(15, 2, -32, 89), Ticks(2.1));
            _ = restored.Observe(
                "woods", Scene(seed: 17), Width, Height,
                new VisualKeyframePose(15, 2, -32, 89), Ticks(2.2));
            var confirmed = restored.Observe(
                "woods", Scene(seed: 17), Width, Height,
                new VisualKeyframePose(15, 2, -32, 89), Ticks(2.3));

            Require(confirmed.IsConfirmed && confirmed.ExactPose == exact,
                "repère rechargé non reconnu exactement");
        });
    }

    private static void RejectsCorruptedSnapshotAtomically()
    {
        var recognizer = NewRecognizer();
        Require(recognizer.AddExactKeyframe(
            "customs",
            Scene(seed: 21),
            Width,
            Height,
            new VisualKeyframePose(1, 0, 2, 30),
            Ticks(1),
            out _),
            "précondition: repère non ajouté");

        var snapshot = recognizer.CreateSnapshot();
        var entry = snapshot.Maps[0].Keyframes[0];
        snapshot.Maps[0].Keyframes[0] = entry with
        {
            BitsBase64 = "AAAA",
        };

        Require(!recognizer.TryImportSnapshot(snapshot),
            "snapshot de descripteur invalide accepté");
        Require(recognizer.KeyframeCountForMap("customs") == 1,
            "un import invalide a effacé la mémoire existante");
    }

    private static void RejectsCorruptedJsonAtomically()
    {
        InTemp(root =>
        {
            var recognizer = NewRecognizer();
            Require(recognizer.AddExactKeyframe(
                "shoreline",
                Scene(seed: 25),
                Width,
                Height,
                new VisualKeyframePose(3, 1, 4, 60),
                Ticks(1),
                out _),
                "précondition: repère non ajouté");

            var path = Path.Combine(root, "broken.json");
            File.WriteAllText(path, "{not-json");
            Require(!VisualKeyframeStore.TryLoad(recognizer, path),
                "JSON corrompu accepté");
            Require(recognizer.KeyframeCountForMap("shoreline") == 1,
                "un JSON corrompu a effacé la mémoire existante");
        });
    }

    private static void UsesPerPlayerPath()
    {
        var a = VisualKeyframeStore.GetPath("player-a");
        var b = VisualKeyframeStore.GetPath("player-b");
        var aAgain = VisualKeyframeStore.GetPath("player-a");

        Require(!string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            "deux joueurs partagent la même mémoire visuelle");
        Require(string.Equals(a, aAgain, StringComparison.OrdinalIgnoreCase),
            "le chemin d'un joueur n'est pas stable");
        Require(Path.IsPathRooted(a) && a.EndsWith(".json", StringComparison.OrdinalIgnoreCase),
            "chemin mémoire visuelle invalide");
    }

    private static void SavesAtomicallyWithoutLeftovers()
    {
        InTemp(root =>
        {
            var path = Path.Combine(root, "atomic.json");
            var recognizer = NewRecognizer();
            Require(recognizer.AddExactKeyframe(
                "reserve",
                Scene(seed: 31),
                Width,
                Height,
                new VisualKeyframePose(-8, 4, 22, 180),
                Ticks(1),
                out _),
                "précondition: repère non ajouté");

            Require(VisualKeyframeStore.TrySave(recognizer, path),
                "sauvegarde atomique refusée");
            Require(File.Exists(path), "fichier final absent");
            Require(!Directory.EnumerateFiles(root, "*.tmp").Any(),
                "fichier temporaire abandonné");
        });
    }

    private static VisualKeyframeRecognizer NewRecognizer() => new(
        new VisualKeyframeRecognizerSettings
        {
            ConfirmationCooldown = TimeSpan.Zero,
        });

    private static byte[] Scene(int seed)
    {
        var frame = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                frame[(y * Width) + x] = Texture(x, y, seed);
            }
        }
        return frame;
    }

    private static byte Texture(int x, int y, int seed)
    {
        unchecked
        {
            var hash = (uint)(x * 0x1f123bb5)
                ^ (uint)(y * 0x5f356495)
                ^ (uint)(seed * 0x6c8e9cf5);
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (byte)(35 + (hash % 181));
        }
    }

    private static long Ticks(double seconds) =>
        (long)Math.Round(seconds * Stopwatch.Frequency);

    private static void InTemp(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RaidPilot-VisualStore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
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
            failures.Add($"{name} — {exception.Message}");
            Console.Error.WriteLine($"ERR {name}: {exception.Message}");
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
