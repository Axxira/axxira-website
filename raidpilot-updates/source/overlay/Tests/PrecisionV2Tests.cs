using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class PrecisionV2Tests
{
    internal const int TestCount = 12;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("V2: échantillon visuel en 160×90", () => {
            Require(PrecisionV2VisualSampler.Width == 160 && PrecisionV2VisualSampler.Height == 90);
            Require(PrecisionV2VisualSampler.CaptureIntervalSeconds >= 0.15);
        }, failures);
        Run("V2: les deux capteurs d'Overlay sont branchés au suivi", () => {
            var type = typeof(OverlayForm);
            var flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            Require(type.GetMethod("FusePrecisionV2", flags) is not null
                && type.GetMethod("InitializePrecisionV2", flags) is not null
                && type.GetMethod("ResetPrecisionV2", flags) is not null
                && type.GetField("_precisionV2Vision", flags) is not null,
                "Precision V2 n'est pas raccordé au suivi de l'Overlay");
        }, failures);
        Run("V2: trame 160×90 alimente le vrai suivi de points", () => {
            using var vision = new VisualMotionGate(new VisualMotionGateSettings {
                SampleWidth = PrecisionV2VisualSampler.Width,
                SampleHeight = PrecisionV2VisualSampler.Height
            });
            var first = SampleScene(0);
            var second = SampleScene(-3);
            var initial = vision.ObserveLuminanceFrame(
                first, PrecisionV2VisualSampler.Width, PrecisionV2VisualSampler.Height);
            var moved = vision.ObserveLuminanceFrame(
                second, PrecisionV2VisualSampler.Width, PrecisionV2VisualSampler.Height);
            Require(initial.Egomotion.State == VisualEgomotionState.Priming
                && moved.Egomotion.IsReliable
                && moved.Egomotion.SceneShiftX < -1,
                "flux 160×90 non mesuré ou décalage inversé");
        }, failures);
        Run("V2: premier flux fiable ne suffit pas à arrêter", () => {
            var f = new PrecisionV2MotionFusion();
            var result = f.Fuse(Frame(1000, VisualEgomotionState.Tracking),
                1000, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
            Require(result == RealtimeMotionEvidence.Unknown);
        }, failures);
        Run("V2: trois scènes immobiles confirment un arrêt après mouvement", () => {
            var f = new PrecisionV2MotionFusion();
            var freq = Stopwatch.Frequency;
            var start = freq * 10;
            f.Fuse(Frame(start, VisualEgomotionState.Tracking),
                start, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
            for (var i = 1; i <= 2; i++)
            {
                var at = start + freq * i / 5;
                var guess = f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                    at, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
                Require(guess == RealtimeMotionEvidence.Unknown);
            }
            var finalAt = start + freq * 3 / 5;
            var last = f.Fuse(Frame(finalAt, VisualEgomotionState.Stationary),
                finalAt, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
            Require(last == RealtimeMotionEvidence.Stationary && f.AcceptedStops > 0);
        }, failures);
        Run("V2: pas de preuve de vie = jamais d'arrêt", () => {
            var f = new PrecisionV2MotionFusion();
            var start = Stopwatch.Frequency * 10;
            for (var i = 0; i < 5; i++)
            {
                var at = start + Stopwatch.Frequency * i / 5;
                var next = f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                    at, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
                Require(next == RealtimeMotionEvidence.Unknown);
            }
        }, failures);
        Run("V2: une rotation désarme l'arrêt visuel", () => {
            var f = Prepared();
            var at = Stopwatch.Frequency * 11;
            Require(f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                at, (nint)1, true, false, RealtimeMotionEvidence.Unknown)
                == RealtimeMotionEvidence.Unknown);
        }, failures);
        Run("V2: sans touche de mouvement on conserve le moteur de base", () => {
            var f = Prepared();
            var at = Stopwatch.Frequency * 11;
            Require(f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                at, (nint)1, false, true, RealtimeMotionEvidence.Moving)
                == RealtimeMotionEvidence.Moving);
        }, failures);
        Run("V2: trame trop ancienne refusée", () => {
            var f = Prepared();
            var at = Stopwatch.Frequency * 11;
            Require(f.Fuse(Frame(at - Stopwatch.Frequency, VisualEgomotionState.Stationary),
                at, (nint)1, true, true, RealtimeMotionEvidence.Unknown)
                == RealtimeMotionEvidence.Unknown);
        }, failures);
        Run("V2: fenêtre EFT différente refusée", () => {
            var f = Prepared();
            var at = Stopwatch.Frequency * 11;
            Require(f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                at, (nint)2, true, true, RealtimeMotionEvidence.Unknown)
                == RealtimeMotionEvidence.Unknown);
        }, failures);
        Run("V2: flux contradictoire ne remplace jamais l'ancienne preuve", () => {
            var f = new PrecisionV2MotionFusion();
            var at = Stopwatch.Frequency * 5;
            var weak = Frame(at, VisualEgomotionState.Stationary, .3);
            Require(f.Fuse(weak, at, (nint)1, true, true,
                RealtimeMotionEvidence.Moving) == RealtimeMotionEvidence.Moving);
            Require(f.RejectedFrames > 0);
        }, failures);
        Run("V2: reset invalide une confirmation précédente", () => {
            var f = Prepared();
            f.Reset();
            var at = Stopwatch.Frequency * 11;
            Require(f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                at, (nint)1, true, true, RealtimeMotionEvidence.Unknown)
                == RealtimeMotionEvidence.Unknown);
        }, failures);
    }

    private static byte[] SampleScene(int shiftX)
    {
        const int width = PrecisionV2VisualSampler.Width;
        const int height = PrecisionV2VisualSampler.Height;
        var result = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                unchecked
                {
                    var key = (uint)((x - shiftX) * 0x1f123bb5)
                        ^ (uint)(y * 0x5f356495)
                        ^ (uint)(1 * 0x6c8e9cf5);
                    key ^= key >> 16;
                    key *= 0x7feb352d;
                    key ^= key >> 15;
                    key *= 0x846ca68b;
                    key ^= key >> 16;
                    result[y * width + x] = (byte)(40 + (key % 161));
                }
            }
        return result;
    }

    private static PrecisionV2MotionFusion Prepared()
    {
        var f = new PrecisionV2MotionFusion();
        var start = Stopwatch.Frequency * 10;
        f.Fuse(Frame(start, VisualEgomotionState.Tracking),
            start, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
        for (var i = 1; i <= 3; i++)
        {
            var at = start + Stopwatch.Frequency * i / 5;
            f.Fuse(Frame(at, VisualEgomotionState.Stationary),
                at, (nint)1, true, true, RealtimeMotionEvidence.Unknown);
        }
        return f;
    }

    private static PrecisionV2Frame Frame(long at, VisualEgomotionState state,
        double confidence = .92)
    {
        var flow = new VisualEgomotionEstimate(state, state == VisualEgomotionState.Tracking ? 2 : 0,
            0, VisualHorizontalDirection.None, 0, confidence,
            30, 25, 20, .8, .3, 0);
        var visual = new VisualMotionObservation(
            state == VisualEgomotionState.Stationary ? VisualMotionState.Stationary : VisualMotionState.Moving,
            state == VisualEgomotionState.Stationary ? VisualMotionState.Stationary : VisualMotionState.Moving,
            1, 1, 0.2, 20, 0, true, true, flow);
        return new PrecisionV2Frame(at, (nint)1, visual, 2);
    }

    private static void Run(string label, Action action, ICollection<string> failures)
    {
        try { action(); Console.WriteLine("OK  " + label); }
        catch (Exception e) { failures.Add(label + ": " + e.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion Precision V2 échouée");
    }
}
