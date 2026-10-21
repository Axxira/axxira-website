using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace RaidPilot.Overlay.Tests;

internal static class SuperKeyframeRecognizerTests
{
    internal const int TestCount = 13;

    private const int Width = HighResolutionVisualSampler.SampleWidth;
    private const int Height = HighResolutionVisualSampler.SampleHeight;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("super-keyframe: exige plusieurs confirmations", RequiresTemporalConsensus, failures);
        Run("super-keyframe: image étrangère rejetée", RejectsUnrelatedScene, failures);
        Run("super-keyframe: exposition globale tolérée", ToleratesExposureShift, failures);
        Run("super-keyframe: faible texture refusée", RejectsTexturelessScene, failures);
        Run("super-keyframe: forte dérive exige incertitude", GlobalClosureRequiresUncertainty, failures);
        Run("super-keyframe: loop closure global confirmé", ConfirmsGlobalLoopClosure, failures);
        Run("super-keyframe: sosies ambigus rejetés", RejectsAmbiguousPlaces, failures);
        Run("super-keyframe: snapshot persistant", SnapshotRoundTrip, failures);
        Run("super-keyframe: mémoire bornée", KeepsDatabaseBounded, failures);
        Run("super-keyframe: store fichier round-trip", StoreRoundTrip, failures);
        Run("super-keyframe: store corrompu non destructif", CorruptStoreDoesNotReplaceMemory, failures);
        Run("super-keyframe: PNG haute résolution", DecodesHighResolutionScreenshot, failures);
        Run("super-keyframe: entrée invalide refusée", RejectsInvalidFrame, failures);
    }

    private static void RequiresTemporalConsensus()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var frame = Scene();
        Require(recognizer.AddExactKeyframe(
            "customs",
            frame,
            new VisualKeyframePose(100, 2, 200, 15),
            1000,
            out _), "repère exact refusé");

        var first = recognizer.Observe(
            "customs",
            frame,
            new VisualKeyframePose(101, 2, 201, 16),
            2000,
            1.0);
        Require(first.State == SuperKeyframeRecognitionState.Candidate,
            "une seule image ne doit pas recaler");

        var interval = (long)(Stopwatch.Frequency * 0.20);
        var second = recognizer.Observe(
            "customs",
            frame,
            new VisualKeyframePose(101, 2, 201, 16),
            2000 + interval,
            1.0);
        Require(second.IsConfirmed,
            "deux observations haute résolution cohérentes devraient confirmer");
        Require(second.ExactPose is { X: 100, Z: 200 },
            "la pose exacte du repère a été perdue");
    }

    private static void RejectsUnrelatedScene()
    {
        var recognizer = new SuperKeyframeRecognizer();
        Require(recognizer.AddExactKeyframe(
            "customs",
            Scene(seed: 1),
            new VisualKeyframePose(0, 0, 0, 0),
            100,
            out _), "précondition");

        var result = recognizer.Observe(
            "customs",
            Scene(seed: 91),
            new VisualKeyframePose(0, 0, 0, 0),
            1000,
            2);
        Require(result.State == SuperKeyframeRecognitionState.NoMatch,
            "une scène étrangère ne doit pas devenir candidate");
    }

    private static void ToleratesExposureShift()
    {
        var recognizer = new SuperKeyframeRecognizer();
        Require(recognizer.AddExactKeyframe(
            "customs",
            Scene(),
            new VisualKeyframePose(10, 0, 10, 0),
            100,
            out _), "précondition");

        var shifted = Scene(brightnessOffset: 18);
        var result = recognizer.Observe(
            "customs",
            shifted,
            new VisualKeyframePose(10, 0, 10, 0),
            1000,
            1);
        Require(result.State == SuperKeyframeRecognitionState.Candidate,
            $"l'exposition globale a cassé le retrieval ({result.CompositeDistance:F3})");
    }

    private static void RejectsTexturelessScene()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var flat = Enumerable.Repeat((byte)90, Width * Height).ToArray();
        Require(!recognizer.AddExactKeyframe(
            "customs",
            flat,
            new VisualKeyframePose(0, 0, 0, 0),
            100,
            out _), "une image uniforme ne doit pas devenir un repère");
    }

    private static void GlobalClosureRequiresUncertainty()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var frame = Scene();
        Require(recognizer.AddExactKeyframe(
            "customs",
            frame,
            new VisualKeyframePose(0, 0, 0, 0),
            100,
            out _), "précondition");

        var result = recognizer.Observe(
            "customs",
            frame,
            new VisualKeyframePose(150, 0, 150, 5),
            1000,
            2.0);
        Require(result.State == SuperKeyframeRecognitionState.NoMatch,
            "une pose encore fiable ne doit pas autoriser un loop closure global");
    }

    private static void ConfirmsGlobalLoopClosure()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var frame = Scene();
        Require(recognizer.AddExactKeyframe(
            "customs",
            frame,
            new VisualKeyframePose(0, 0, 0, 0),
            100,
            out _), "précondition");

        var interval = (long)(Stopwatch.Frequency * 0.20);
        SuperKeyframeRecognition result = default;
        for (var i = 0; i < 3; i++)
        {
            result = recognizer.Observe(
                "customs",
                frame,
                new VisualKeyframePose(150, 1, 150, 8),
                2000 + (interval * i),
                12.0);
        }

        Require(result.IsConfirmed,
            "trois preuves fortes haute résolution devraient fermer la boucle");
        Require(result.GlobalLoopClosure,
            "la confirmation devrait être marquée globale");
    }

    private static void RejectsAmbiguousPlaces()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var frame = Scene();
        Require(recognizer.AddExactKeyframe(
            "customs",
            frame,
            new VisualKeyframePose(0, 0, 0, 0),
            100,
            out _), "repère A");
        Require(recognizer.AddExactKeyframe(
            "customs",
            Shift(frame, 1, 0),
            new VisualKeyframePose(70, 0, 70, 0),
            200,
            out _), "repère B");

        var result = recognizer.Observe(
            "customs",
            frame,
            new VisualKeyframePose(35, 0, 35, 0),
            1000,
            12.0);
        Require(!result.IsConfirmed,
            "un premier frame ambigu ne doit jamais confirmer");
    }

    private static void SnapshotRoundTrip()
    {
        var source = new SuperKeyframeRecognizer();
        Require(source.AddExactKeyframe(
            "reserve",
            Scene(seed: 4),
            new VisualKeyframePose(12, -2, 34, 275),
            100,
            out _), "repère source");

        var snapshot = source.CreateSnapshot();
        var restored = new SuperKeyframeRecognizer();
        Require(restored.TryImportSnapshot(snapshot),
            "snapshot refusé");
        Require(restored.KeyframeCountForMap("reserve") == 1,
            "repère non restauré");
    }

    private static void KeepsDatabaseBounded()
    {
        var recognizer = new SuperKeyframeRecognizer();
        var accepted = 0;
        for (var index = 0; index < 110; index++)
        {
            if (recognizer.AddExactKeyframe(
                "customs",
                Scene(seed: index + 10),
                new VisualKeyframePose(index * 3.0, 0, index * 2.0, index % 360),
                index + 1,
                out _))
            {
                accepted++;
            }
        }

        Require(accepted >= 96, "pas assez de scènes de test utilisables");
        Require(recognizer.KeyframeCountForMap("customs") == 96,
            "la base doit rester bornée à 96 SuperKeyframes par carte");
    }

    private static void StoreRoundTrip()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RaidPilot-SuperKeyframe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new SuperKeyframeRecognizer();
            Require(source.AddExactKeyframe(
                "woods",
                Scene(seed: 123),
                new VisualKeyframePose(42, 3, -18, 92),
                100,
                out _), "repère source refusé");

            var path = Path.Combine(root, "super.json");
            Require(SuperKeyframeStore.TrySave(source, path),
                "sauvegarde SuperKeyframe refusée");
            Require(new FileInfo(path).Length is > 0 and < 12 * 1024 * 1024,
                "taille de base anormale");

            var restored = new SuperKeyframeRecognizer();
            Require(SuperKeyframeStore.TryLoad(restored, path),
                "rechargement SuperKeyframe refusé");
            Require(restored.KeyframeCountForMap("woods") == 1,
                "repère perdu au round-trip");

            var a = SuperKeyframeStore.GetPath("player-a");
            var b = SuperKeyframeStore.GetPath("player-b");
            Require(!string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
                "deux joueurs partagent le même fichier SuperKeyframe");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CorruptStoreDoesNotReplaceMemory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RaidPilot-SuperKeyframe-Corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var recognizer = new SuperKeyframeRecognizer();
            Require(recognizer.AddExactKeyframe(
                "reserve",
                Scene(seed: 55),
                new VisualKeyframePose(1, 2, 3, 4),
                100,
                out _), "précondition");
            var path = Path.Combine(root, "broken.json");
            File.WriteAllText(path, "{not-json");

            Require(!SuperKeyframeStore.TryLoad(recognizer, path),
                "un store corrompu ne doit pas être accepté");
            Require(recognizer.KeyframeCountForMap("reserve") == 1,
                "un échec de chargement a détruit la mémoire saine");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void DecodesHighResolutionScreenshot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "RaidPilot-SuperKeyframe-Png-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "frame.png");
            using (var bitmap = new Bitmap(320, 180))
            {
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var value = (x * 3 + y * 5) & 255;
                        bitmap.SetPixel(x, y, Color.FromArgb(
                            value,
                            (value + 47) & 255,
                            (value + 103) & 255));
                    }
                }
                bitmap.Save(path, ImageFormat.Png);
            }

            Require(
                HighResolutionVisualSampler.TryCreateLuminanceFrameFromImage(
                    path,
                    out var frame),
                "le PNG haute résolution n'a pas été décodé");
            Require(frame.Length == Width * Height,
                "la trame 160x90 a une taille invalide");
            Require(frame.Distinct().Count() > 32,
                "le décodage PNG a perdu la texture");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RejectsInvalidFrame()
    {
        var recognizer = new SuperKeyframeRecognizer();
        RequireThrows<ArgumentException>(() =>
            recognizer.AddExactKeyframe(
                "customs",
                new byte[12],
                new VisualKeyframePose(0, 0, 0, 0),
                0,
                out _));
    }

    private static byte[] Scene(
        int seed = 1,
        int brightnessOffset = 0)
    {
        var frame = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var value = Texture(x, y, seed);
                frame[(y * Width) + x] = (byte)Math.Clamp(
                    value + brightnessOffset,
                    0,
                    255);
            }
        }
        return frame;
    }

    private static byte[] Shift(byte[] source, int dx, int dy)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var sx = Math.Clamp(x - dx, 0, Width - 1);
                var sy = Math.Clamp(y - dy, 0, Height - 1);
                result[(y * Width) + x] = source[(sy * Width) + sx];
            }
        }
        return result;
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
            return (byte)(32 + (hash % 193));
        }
    }

    private static void RequireThrows<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(
            $"exception {typeof(T).Name} attendue");
    }

    private static void Run(
        string name,
        Action test,
        ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine("OK  " + name);
        }
        catch (Exception exception)
        {
            failures.Add(name + " — " + exception.Message);
            Console.Error.WriteLine("ERR " + name + ": " + exception.Message);
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