using System.Diagnostics;
using System.Windows.Forms;

namespace RaidPilot.Overlay.Tests;

internal static class PrecisionV2Tests
{
    internal const int TestCount = 41;

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
        Run("V2.2: grand décalage horizontal suivi en 160×90", () => {
            var flow = new VisualEgomotionEstimator(
                PrecisionV2VisualSampler.EnhancedFlowSettings);
            flow.Observe(SampleScene(0), 160, 90);
            var move = flow.Observe(SampleScene(-9), 160, 90);
            Require(move.IsReliable && Math.Abs(move.SceneShiftX + 9) <= 1,
                $"recherche longue portée insuffisante: {move.State}, dx={move.SceneShiftX}");
        }, failures);
        Run("V2.2: profil de flux spécialisé sans modifier V1", () => {
            var profile = PrecisionV2VisualSampler.EnhancedFlowSettings;
            var legacy = new VisualEgomotionSettings();
            Require(profile.HorizontalSearchRadius > legacy.HorizontalSearchRadius
                && profile.VerticalSearchRadius > legacy.VerticalSearchRadius
                && profile.GridColumns > legacy.GridColumns
                && legacy.HorizontalSearchRadius == 6
                && legacy.GridColumns == 8,
                "la configuration basse résolution a été modifiée par erreur");
        }, failures);
        Run("V3: capture GPU désactivée sans choix utilisateur", () => {
            using var sensor = new PrecisionV2VisualSampler();
            Require(!sensor.GpuCaptureSelected
                && sensor.Backend.Contains("GDI", StringComparison.Ordinal),
                "le démarrage a activé le capteur GPU sans consentement");
        }, failures);
        Run("V3: activation GPU par choix explicite, désactivation réversible", () => {
            using var sensor = new PrecisionV2VisualSampler();
            sensor.SelectGpuCapture(true);
            Require(sensor.GpuCaptureSelected && sensor.Backend.Contains("WGC"),
                "l'activation ne sélectionne pas le capteur GPU");
            sensor.SelectGpuCapture(false);
            Require(!sensor.GpuCaptureSelected && sensor.Backend.Contains("GDI"),
                "le retour au suivi GDI ne fonctionne pas");
        }, failures);
        Run("V3: refus d'une fenêtre nulle avant chargement natif", () => {
            using var adapter = new PrecisionV3WindowsCapture();
            var result = adapter.Read(nint.Zero, out var pixels);
            Require(result == WgcFrameStatus.Unavailable && pixels.Length == 0,
                "l'appel GPU n'a pas refusé une fenêtre invalide");
        }, failures);
        Run("V3: capteur redémarrable après repli sans forcer le mode GPU", () => {
            using var sensor = new PrecisionV2VisualSampler();
            sensor.SelectGpuCapture(true);
            sensor.Reset();
            Require(sensor.GpuCaptureSelected, "F8 a désactivé le choix GPU");
            sensor.SelectGpuCapture(false);
            sensor.Reset();
            Require(!sensor.GpuCaptureSelected
                && sensor.GpuFrames == 0
                && sensor.GpuFallbacks == 0,
                "un nouveau suivi a conservé une statistique GPU fictive");
        }, failures);
        Run("V3.1: WGC 16:9 rapproche la géométrie client réelle", () => {
            Require(VisualCaptureGeometryPolicy.TryMatchViewport(
                1920,1080,1920,1080,out var v)
                && Math.Abs(v.AspectRatio-16.0/9.0) < 1e-9);
        }, failures);
        Run("V3.1: WGC accepte les résolutions 1440p et 4K", () => {
            Require(VisualCaptureGeometryPolicy.TryMatchViewport(
                2560,1440,2560,1440,out _)
                && VisualCaptureGeometryPolicy.TryMatchViewport(
                    3840,2160,3840,2160,out _));
        }, failures);
        Run("V3.1: ultralarge 21:9 et 32:9 calibrables", () => {
            Require(VisualCaptureGeometryPolicy.TryMatchViewport(
                3440,1440,3440,1440,out _)
                && VisualCaptureGeometryPolicy.TryMatchViewport(
                    5120,1440,5120,1440,out _));
        }, failures);
        Run("V3.1: petites bordures WGC ne modifient pas l'aspect", () => {
            Require(VisualCaptureGeometryPolicy.TryMatchViewport(
                1920,1080,1936,1090,out var v)
                && v.ClientWidth==1920 && v.ClientHeight==1080);
        }, failures);
        Run("V3.1: capture étirée ou décorée rejetée", () => {
            Require(!VisualCaptureGeometryPolicy.TryMatchViewport(
                1920,1080,1024,768,out _)
                && !VisualCaptureGeometryPolicy.TryMatchViewport(
                3440,1440,1920,1080,out _)
                && !VisualCaptureGeometryPolicy.TryMatchViewport(
                2560,1440,0,0,out _));
        }, failures);
        Run("V3.1: FOV recadré 21:9 supérieur à celui du 16:9", () => {
            var narrow=VisualCaptureGeometryPolicy.CalculateEffectiveHorizontalFov(
                1920,1080,50);
            var wide=VisualCaptureGeometryPolicy.CalculateEffectiveHorizontalFov(
                3440,1440,50);
            var full=EftGameViewSettings.CalculateHorizontalFieldOfViewDegrees(
                50,1920.0/1080);
            Require(wide>narrow+8 && narrow<full && narrow>50,
                "FOV horizontal non adapté au format de la fenêtre");
        }, failures);
        Run("V3.1: un changement de résolution invalide l'ancienne trame", () => {
            using var gate=new VisualMotionGate(new VisualMotionGateSettings {
                SampleWidth=160,SampleHeight=90,
                EgomotionSettings=PrecisionV2VisualSampler.EnhancedFlowSettings
            });
            gate.ConfigureVerticalFieldOfView(50);
            var priming=gate.ObserveLuminanceFrame(SampleScene(0),160,90,1920,1080);
            var moved=gate.ObserveLuminanceFrame(SampleScene(-3),160,90,1920,1080);
            var at21By9=gate.ObserveLuminanceFrame(SampleScene(-6),160,90,3440,1440);
            Require(priming.Egomotion.State==VisualEgomotionState.Priming
                && moved.Egomotion.IsReliable
                && at21By9.Egomotion.State==VisualEgomotionState.Priming
                && !at21By9.CaptureProvenLive,
                "ancienne odométrie réutilisée après un changement de format");
            var wanted=VisualCaptureGeometryPolicy.CalculateEffectiveHorizontalFov(
                3440,1440,50,new VisualMotionGateSettings{
                    SampleWidth=160,SampleHeight=90,
                    EgomotionSettings=PrecisionV2VisualSampler.EnhancedFlowSettings
                });
            Require(Math.Abs(gate.EgomotionHorizontalFieldOfViewDegrees-wanted)<0.01,
                "le suivi 160×90 n'a pas pris le nouveau FOV");
        }, failures);
        Run("V3.1: un changement de FOV invalide le suivi visuel", () => {
            using var gate=new VisualMotionGate(new VisualMotionGateSettings{
                SampleWidth=160,SampleHeight=90
            });
            gate.ConfigureVerticalFieldOfView(50);
            gate.ObserveLuminanceFrame(SampleScene(0),160,90,1920,1080);
            gate.ObserveLuminanceFrame(SampleScene(-3),160,90,1920,1080);
            var before=gate.EgomotionHorizontalFieldOfViewDegrees;
            gate.ConfigureVerticalFieldOfView(70);
            var prime=gate.ObserveLuminanceFrame(SampleScene(-6),160,90,1920,1080);
            Require(prime.Egomotion.State==VisualEgomotionState.Priming
                && gate.EgomotionHorizontalFieldOfViewDegrees>before+10,
                "nouveau FOV traité avec la géométrie précédente");
        }, failures);
        Run("V3.1: le garde GDI 64×36 reste indépendant de V3", () => {
            using var gate=new VisualMotionGate();
            gate.ConfigureVerticalFieldOfView(50);
            Require(gate.SampleWidth==64 && gate.SampleHeight==36
                && new VisualEgomotionSettings().HorizontalSearchRadius==6,
                "ancienne voie de secours altérée par Precision V3");
        }, failures);
        Run("V3.2: attente d'une trame ne brise pas le suivi WGC", () => {
            for (var pending = 1; pending < PrecisionV3ContinuityPolicy.MaximumPendingPolls; pending++)
                Require(PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(1, pending),
                    "interruption GPU brève force une nouvelle calibration de scène");
            Require(!PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(
                1, PrecisionV3ContinuityPolicy.MaximumPendingPolls),
                "capture GPU bloquée conservée trop longtemps");
        }, failures);
        Run("V3.2: GDI reste actif au démarrage du capteur WGC", () => {
            Require(!PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(0,1)
                && !PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(-1,1)
                && !PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(1,0),
                "le capteur GPU inconnu désactive le repli GDI");
        }, failures);
        Run("V3.2: après une longue pause la scène repart du premier repère", () => {
            var start = Stopwatch.Frequency * 100L;
            Require(!PrecisionV3ContinuityPolicy.MustReprimeAfterGap(
                start, start + Stopwatch.Frequency / 4),
                "flux normal réinitialisé trop tôt");
            Require(PrecisionV3ContinuityPolicy.MustReprimeAfterGap(
                start, start + Stopwatch.Frequency),
                "ancien flux WGC utilisé après une interruption de 1 seconde");
            Require(PrecisionV3ContinuityPolicy.MustReprimeAfterGap(0,start),
                "première image WGC non initialisée");
        }, failures);
        Run("V3.2: ancienne image stationnaire ne bloque pas le clavier", () => {
            var f=Prepared();
            var at=Stopwatch.Frequency * 10L + Stopwatch.Frequency * 3 / 5;
            var oldStop=Frame(at,VisualEgomotionState.Stationary);
            var tooLate=at + Stopwatch.Frequency * 2 / 5; // 400ms < 480ms stale limit
            Require(f.Fuse(oldStop, tooLate, (nint)1, true, true,
                RealtimeMotionEvidence.Unknown) == RealtimeMotionEvidence.Unknown,
                "ancienne capture stationnaire bloque le mouvement commandé");
            Require(f.Fuse(oldStop, at + Stopwatch.Frequency/5, (nint)1,
                true, true, RealtimeMotionEvidence.Unknown)
                == RealtimeMotionEvidence.Stationary,
                "la preuve stationnaire récente est incorrectement rejetée");
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
        RunVisualReaderTests(failures);
        RunIntelligenceTests(failures);
    }


    private static (VisualReaderYawSync reader, RawKeyboardTimeline keyboard,
        PrecisionYawTimeline mouse, long start, long end) ReaderScenario()
    {
        var start=Stopwatch.Frequency*10L;
        var end=start+Stopwatch.Frequency/5;
        var keyboard=new RawKeyboardTimeline();
        foreach(var key in new[]{Keys.W,Keys.S,Keys.A,Keys.D})
            keyboard.Synchronize(key,false);
        var mouse=new PrecisionYawTimeline();
        mouse.Record(start+Stopwatch.Frequency/10,100,false);
        var reader=new VisualReaderYawSync();
        Require(!reader.TryRead(ReaderFrame(start),start,(nint)1,
            keyboard,mouse,new[]{Keys.W,Keys.S,Keys.A,Keys.D},out _,out _),
            "première image ne définit pas une rotation relative");
        return (reader,keyboard,mouse,start,end);
    }

    private static PrecisionV2Frame ReaderFrame(long at)
    {
        var original=Frame(at,VisualEgomotionState.Tracking);
        var flow=original.Visual.Egomotion with {
            EstimatedYawDeltaDegrees=5.0,
            SceneShiftX=-4,
            SceneShiftY=0.1
        };
        return original with { Visual=original.Visual with { Egomotion=flow } };
    }

    private static bool ReadReader(
        VisualReaderYawSync reader,RawKeyboardTimeline keyboard,
        PrecisionYawTimeline mouse,long at,long now,PrecisionV2Frame? frame=null)
        => reader.TryRead(frame??ReaderFrame(at),now,(nint)1,keyboard,mouse,
            new[]{Keys.W,Keys.S,Keys.A,Keys.D},out _,out _);

    private static void RunVisualReaderTests(ICollection<string> failures)
    {
        Run("Reader V2: deux images live + yaw raw correspondent",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            Require(ReadReader(reader,keys,mouse,end,end)
                && reader.Accepted==1 && reader.HasRecentReliableFrame(end),
                "rotation visuelle correctement horodatée non détectée");
        },failures);
        Run("Reader V2: même image non réutilisée",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            Require(ReadReader(reader,keys,mouse,end,end));
            Require(!ReadReader(reader,keys,mouse,end,end) && reader.Accepted==1,
                "une vieille trame est recomptée");
        },failures);
        Run("Reader V2: interaction ou ADS invalide une rotation",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            reader.ObserveActivity(false,false,true);
            Require(!ReadReader(reader,keys,mouse,end,end));
            reader.Reset();
            var start=end+Stopwatch.Frequency;
            Require(!ReadReader(reader,keys,mouse,start,start));
            reader.ObserveActivity(false,true,false);
            Require(!ReadReader(reader,keys,mouse,start+Stopwatch.Frequency/5,
                start+Stopwatch.Frequency/5));
        },failures);
        Run("Reader V2: tap de mouvement entre deux images exclu",()=>{
            var (reader,keys,mouse,start,end)=ReaderScenario();
            keys.Record(Keys.W,true,start+Stopwatch.Frequency/20);
            keys.Record(Keys.W,false,start+Stopwatch.Frequency/10);
            Require(!ReadReader(reader,keys,mouse,end,end)
                && reader.LastStatus.Contains("clavier",StringComparison.Ordinal),
                "le tap court est traité comme rotation pure");
        },failures);
        Run("Reader V2: image ancienne ou lente exclue",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            Require(!ReadReader(reader,keys,mouse,end,end+Stopwatch.Frequency/2));
            reader.Reset();
            var later=end+Stopwatch.Frequency;
            Require(!ReadReader(reader,keys,mouse,later,later));
            var slow=ReaderFrame(later+Stopwatch.Frequency/5) with {
                ProcessingMilliseconds=150
            };
            Require(!ReadReader(reader,keys,mouse,slow.CapturedAt,slow.CapturedAt,slow));
        },failures);
        Run("Reader V2: flux peu fiable interdit",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            var original=ReaderFrame(end);
            var bad=original with {Visual=original.Visual with {
                Egomotion=original.Visual.Egomotion with {Confidence=0.12}
            }};
            Require(!ReadReader(reader,keys,mouse,end,end,bad));
        },failures);
        Run("Reader V2: mouvement visuel opposé au raw refusé",()=>{
            var (reader,keys,mouse,_,end)=ReaderScenario();
            var original=ReaderFrame(end);
            var wrong=original with {Visual=original.Visual with {
                Egomotion=original.Visual.Egomotion with {EstimatedYawDeltaDegrees=-5}
            }};
            Require(!ReadReader(reader,keys,mouse,end,end,wrong));
        },failures);
    }

    private static void RunIntelligenceTests(ICollection<string> failures)
    {
        Run("Intelligence: image ancienne conserve estimation de base",()=>{
            var e=new RaidPilotIntelligenceEngine();
            var t=Stopwatch.Frequency*10;
            var old=Frame(t,VisualEgomotionState.Stationary);
            Require(e.Evaluate(old,t+Stopwatch.Frequency,(nint)1,true,true,
                RealtimeMotionEvidence.Moving,RealtimeMotionEvidence.Stationary)
                ==RealtimeMotionEvidence.Moving);
        },failures);
        Run("Intelligence: mouvement visuel refuse faux arrêt",()=>{
            var e=new RaidPilotIntelligenceEngine();
            var t=Stopwatch.Frequency*10;
            var moving=Frame(t,VisualEgomotionState.Tracking);
            Require(e.Evaluate(moving,t,(nint)1,true,true,
                RealtimeMotionEvidence.Unknown,RealtimeMotionEvidence.Stationary)
                ==RealtimeMotionEvidence.Moving);
        },failures);
        Run("Intelligence: arrêt confirmé conserve veto",()=>{
            var e=new RaidPilotIntelligenceEngine();
            var t=Stopwatch.Frequency*10;
            var still=Frame(t,VisualEgomotionState.Stationary);
            Require(e.Evaluate(still,t,(nint)1,true,true,
                RealtimeMotionEvidence.Unknown,RealtimeMotionEvidence.Stationary)
                ==RealtimeMotionEvidence.Stationary);
        },failures);
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

    private static void Require(bool condition, string? explanation = null)
    {
        if (!condition) throw new InvalidOperationException(
            explanation ?? "Assertion Precision V2 échouée");
    }
}
