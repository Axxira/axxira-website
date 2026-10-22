using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class PrecisionEngineTests
{
    internal const int TestCount = 30;
    private static ScreenshotObservation Anchor(double x = 0, double yaw = 0) => new("synthetic-native", x, 0, 0, 0, 0, 0, 1, yaw);
    private static RawKeyboardTimeline Keyboard(bool forward = false, int capacity = 512)
    {
        var k = new RawKeyboardTimeline(capacity);
        foreach (var key in new[] { Keys.W, Keys.S, Keys.A, Keys.D }) k.Synchronize(key, key == Keys.W && forward);
        return k;
    }
    private static RealtimePoseEstimator Estimator() { var e = new RealtimePoseEstimator(); e.Anchor(Anchor()); return e; }
    private static bool Integrate(RealtimePoseEstimator e, RawKeyboardTimeline k, PrecisionYawTimeline m,
        long start, long end, int counts, out PrecisionMotionResult r) => new PrecisionMotionIntegrator(1000).TryAdvance(
            e, k, m, start, end, Keys.W, Keys.S, Keys.A, Keys.D, false, false, 1, RealtimeMotionEvidence.Unknown, counts, out r);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion de précision échouée"); }
    internal static void RunAll(ICollection<string> failures)
    {
        Run("activité opposée non classée comme repos", () => Require(PrecisionMotionIntegrator.MovementActivity(.3,.3,0,0)>0), failures);
        Run("aucune entrée conserve le vrai repos", () => Require(PrecisionMotionIntegrator.MovementActivity(0,0,0,0)==0), failures);
        Run("inversion de marche indépendante du pas temporel", () => {
            var a=Estimator();var b=Estimator();a.Advance(.02,1,0,false,false,0,false);b.Advance(.02,1,0,false,false,0,false);
            a.Advance(.04,-1,0,false,false,0,false);for(int i=0;i<40;i++)b.Advance(.001,-1,0,false,false,0,false);
            Require(Math.Abs(a.CurrentPose!.Value.Z-b.CurrentPose!.Value.Z)<1e-8);
        }, failures);
        Run("précision conserve les taps opposés dans le même tick", () => {
            var k=Keyboard(); k.Record(Keys.W,true,1005);k.Record(Keys.W,false,1020);k.Record(Keys.S,true,1030);k.Record(Keys.S,false,1045);
            Require(Integrate(Estimator(),k,new(),1000,1050,0,out var r)); Require(r.PathMeters>0&&Math.Abs(r.Pose.Z)>0.00001);
        }, failures);
        Run("précision sépare rotation aller et retour", () => {
            var m=new PrecisionYawTimeline();m.Record(1010,1000,false);m.Record(1040,-1000,false);
            Require(Integrate(Estimator(),Keyboard(true),m,1000,1050,0,out var r));Require(r.Pose.X>0.0001&&Math.Abs(r.Pose.YawDegrees)<1e-8);
        }, failures);
        Run("précision différencie début et fin de rotation", () => {
            var a=new PrecisionYawTimeline();a.Record(1005,900,false);var b=new PrecisionYawTimeline();b.Record(1045,900,false);
            Integrate(Estimator(),Keyboard(true),a,1000,1050,900,out var x);Integrate(Estimator(),Keyboard(true),b,1000,1050,900,out var y);
            Require(Math.Abs(x.Pose.X-y.Pose.X)>0.0001);
        }, failures);
        Run("précision conserve les sensibilités par échantillon", () => {
            var m=new PrecisionYawTimeline();m.Record(1010,100,false);m.Record(1020,100,true);
            Integrate(Estimator(),Keyboard(),m,1000,1050,200,out var r);Require(Math.Abs(r.Pose.YawDegrees-7.5)<1e-8);
        }, failures);
        Run("précision refuge sans rotation complètement horodatée", () => {
            var e=Estimator();Require(!Integrate(e,Keyboard(),new(),1000,1050,100,out _));Require(e.CurrentPose!.Value.X==0&&e.CurrentPose.Value.YawDegrees==0);
        }, failures);
        Run("précision refuse un historique clavier tronqué avant mutation", () => {
            var k=Keyboard(false,2);for(int i=1;i<8;i++)k.Record(Keys.W,i%2==1,1000+i);
            var e=Estimator();Require(!Integrate(e,k,new(),1000,1050,0,out _)&&e.CurrentPose!.Value.Z==0);
        }, failures);
        Run("rotation bornée refuse une plage évincée", () => {
            var m=new PrecisionYawTimeline(2);for(int i=1;i<6;i++)m.Record(1000+i,1,false);
            Require(m.Count==2&&!m.CopyInterval(1000,1010,new()));
        }, failures);
        Run("rotation reset retire les entrées antérieures", () => {var m=new PrecisionYawTimeline();m.Record(1001,300,false);m.Reset();var l=new List<PrecisionYawTimeline.Sample>();Require(m.CopyInterval(1000,1050,l)&&l.Count==0);}, failures);
        Run("précision refuse une longue interruption", () => Require(!Integrate(Estimator(),Keyboard(true),new(),1000,3000,0,out _)), failures);
        Run("précision affine à une ms uniquement si nécessaire", () => {
            var m=new PrecisionYawTimeline();m.Record(1015,5,false);Integrate(Estimator(),Keyboard(true),m,1000,1050,5,out var a);
            Integrate(Estimator(),Keyboard(true),new(),1000,1050,0,out var b);Require(a.Slices==50&&b.Slices==10);
        }, failures);
        Run("précision refuse le sur-budget sans modifier la pose", () => {
            var e=Estimator();var m=new PrecisionYawTimeline();m.Record(1100,5,false);
            Require(!Integrate(e,Keyboard(true),m,1000,1750,5,out _)&&e.CurrentPose!.Value.Z==0);
        }, failures);
        Run("précision respecte le budget de sous-pas", () => {Require(Integrate(Estimator(),Keyboard(true),new(),1000,1750,0,out var r));Require(r.Slices==150);}, failures);
        Run("immobilité confirmée ne crée pas de déplacement précis", () => {
            var e=Estimator();var integrator=new PrecisionMotionIntegrator(1000);
            Require(integrator.TryAdvance(e,Keyboard(true),new(),1000,1050,Keys.W,Keys.S,Keys.A,Keys.D,false,false,1,RealtimeMotionEvidence.Stationary,0,out var r)&&r.PathMeters==0);
        }, failures);
        Run("pause retire l'ancien élan", () => {var e=Estimator();e.Advance(1,1,0,false,false,0,false);var p=e.CurrentPose!.Value;e.SuspendMotion();e.Advance(.1,0,0,false,false,0,false);Require(e.CurrentPose!.Value.Z==p.Z);}, failures);
        Run("temps non observé augmente l'incertitude", () => {var e=Estimator();var u=e.CurrentPose!.Value.PositionUncertaintyMeters;e.SuspendMotion(3);Require(e.CurrentPose!.Value.PositionUncertaintyMeters>u+10&&e.CurrentPose.Value.Z==0);}, failures);
        Run("calibration exige plusieurs segments", () => {var c=new PrecisionCalibration();Require(c.Propose("walk",2.4,2.2,1.2,3.5)is null);Require(c.Propose("walk",2.42,2.2,1.2,3.5)is null);Require(c.Propose("walk",2.38,2.2,1.2,3.5)is not null);}, failures);
        Run("calibration limite chaque correction à trois pour cent", () => {var c=new PrecisionCalibration();for(int i=0;i<2;i++)c.Propose("walk",3,2.2,1.2,3.5);var v=c.Propose("walk",3,2.2,1.2,3.5);Require(v is {} x&&x<=2.2*1.03+1e-12);}, failures);
        Run("calibration rejette un échantillon aberrant isolé", () => {var c=new PrecisionCalibration();for(int i=0;i<3;i++)c.Propose("walk",2.2,2.2,1.2,3.5);Require(c.Propose("walk",3.1,2.2,1.2,3.5)is null&&c.Rejected>0);}, failures);
        Run("calibration refuse les chemins courbes", () => Require(!PrecisionCalibration.IsSpeedSegmentEligible(20,19,30,1,0,true)), failures);
        Run("calibration refuse les escaliers et données interrompues", () => {Require(!PrecisionCalibration.IsSpeedSegmentEligible(20,19,20,1,4,true));Require(!PrecisionCalibration.IsSpeedSegmentEligible(20,19,20,1,0,false));}, failures);
        Run("calibration accepte un segment rectiligne admissible", () => Require(PrecisionCalibration.IsSpeedSegmentEligible(20,19,20,.999,0,true)), failures);
        Run("calibration ne mélange pas hip et ADS en un paramètre", () => {
            var c=new PrecisionCalibration();var s=new RealtimePoseEstimatorSettings();for(int i=0;i<6;i++)s=c.Learn(s,Anchor(),Anchor(yaw:45),null,800,200,800,200,0,0,0,true);
            Require(s.HipYawDegreesPerMouseCount==.05&&s.AdsYawDegreesPerMouseCount==.025);
        }, failures);
        Run("calibration refuse rotations qui se compensent", () => {
            var c=new PrecisionCalibration();var s=new RealtimePoseEstimatorSettings();for(int i=0;i<6;i++)s=c.Learn(s,Anchor(),Anchor(yaw:15),null,300,0,2400,0,0,0,0,true);Require(s.HipYawDegreesPerMouseCount==.05);
        }, failures);
        Run("résidu avant recalage mesuré sans coordonnées persistantes", () => {var r=new PrecisionResiduals();r.Observe(new(3,0,4,359,1,1),Anchor(yaw:1));Require(r.Count==1&&r.Last?.Meters==5&&r.Last?.YawDegrees==2);}, failures);
        Run("résidus bornés puis remis à zéro", () => {var r=new PrecisionResiduals();for(int i=0;i<50;i++)r.Observe(new(i,0,0,0,1,1),Anchor());Require(r.Count==32);r.Reset();Require(r.Count==0&&r.Last is null);}, failures);
        Run("précision trajectoire droite conserve la physique précédente", () => {
            var a=Estimator();a.Advance(.05,1,0,false,false,0,false);Integrate(Estimator(),Keyboard(true),new(),1000,1050,0,out var b);
            Require(Math.Abs(a.CurrentPose!.Value.Z-b.Pose.Z)<1e-9);
        }, failures);
        Run("précision sans translation verticale inventée", () => {
            var e=Estimator();e.Anchor(new("native",0,42,0,0,0,0,1,0));Integrate(e,Keyboard(true),new(),1000,1050,0,out var r);Require(r.Pose.Y==42);
        }, failures);
    }

    internal readonly record struct InputEvent(int AtMs, Keys Key, bool Down, int Mouse);
    internal static object RunBenchmarks()
    {
        var rows=new List<object>();var stopwatch=new Stopwatch();
        foreach(var name in new[]{"straight","quick-taps","turn-out-and-back","turn-while-moving","uneven-ui-ticks"})
        {
            var events=new List<InputEvent>();
            bool held=name is "straight" or "turn-out-and-back" or "turn-while-moving" or "uneven-ui-ticks";
            for(int t=0;t<10000;t+=100)
            {
                if(name=="quick-taps") {events.Add(new(t+5,Keys.W,true,0));events.Add(new(t+20,Keys.W,false,0));events.Add(new(t+30,Keys.S,true,0));events.Add(new(t+45,Keys.S,false,0));}
                if(name=="turn-out-and-back") {events.Add(new(t+10,Keys.None,false,800));events.Add(new(t+40,Keys.None,false,-800));}
                if(name=="turn-while-moving") {events.Add(new(t+5,Keys.None,false,120));events.Add(new(t+80,Keys.None,false,120));}
                if(name=="uneven-ui-ticks") {events.Add(new(t+5,Keys.None,false,300));events.Add(new(t+60,Keys.None,false,-180));}
            }
            var baseline=Replay(events,held,false,name=="uneven-ui-ticks"?137:50,1);
            var reference=ReplayReference(events,held);
            stopwatch.Restart();var improved=Replay(events,held,true,name=="uneven-ui-ticks"?137:50,1);stopwatch.Stop();
            double Error(RealtimePose p, RealtimePose expected)=>Math.Sqrt(Math.Pow(p.X-expected.X,2)+Math.Pow(p.Z-expected.Z,2));
            double[] Errors(Dictionary<int,RealtimePose> trace)=>trace.Select(kv=>Error(kv.Value,reference.Trace[kv.Key])).ToArray();
            var oldErrors=Errors(baseline.Trace);var newErrors=Errors(improved.Trace);
            rows.Add(new{name,simulatedSeconds=10,baselineMeters=Error(baseline.Pose,reference.Pose),precisionMeters=Error(improved.Pose,reference.Pose),
                baselineRmsMeters=Math.Sqrt(oldErrors.Average(x=>x*x)),precisionRmsMeters=Math.Sqrt(newErrors.Average(x=>x*x)),
                baselineMaxMeters=oldErrors.Max(),precisionMaxMeters=newErrors.Max(),replayMilliseconds=stopwatch.Elapsed.TotalMilliseconds,
                reference="Same kinematic model replayed at 0.1 ms; not actual EFT position"});
        }
        return new { scenarios=rows, hotPath=BenchmarkHotPath(), actualGameMeasured=false };
    }
    private static (RealtimePose Pose, Dictionary<int,RealtimePose> Trace) Replay(List<InputEvent> events,bool held,bool precision,int period,int unused)
    {
        var k=Keyboard(held);var m=new PrecisionYawTimeline();var e=Estimator();var integrator=new PrecisionMotionIntegrator(1000);
        var legacy=new Legacy091.RealtimePoseEstimator();legacy.Anchor(Anchor());
        var trace=new Dictionary<int,RealtimePose>();
        int previous=0,index=0;
        while(previous<10000)
        {
            int end=Math.Min(10000,previous+period),mouse=0;
            while(index<events.Count&&events[index].AtMs<=end)
            {
                var item=events[index++];if(item.Key!=Keys.None)k.Record(item.Key,item.Down,1000+item.AtMs);
                if(item.Mouse!=0){mouse+=item.Mouse;m.Record(1000+item.AtMs,item.Mouse,false);}
            }
            if(precision)
            {
                if(!integrator.TryAdvance(e,k,m,1000+previous,1000+end,Keys.W,Keys.S,Keys.A,Keys.D,false,false,1,RealtimeMotionEvidence.Unknown,mouse,out _))
                    throw new InvalidOperationException("Benchmark fallback: "+integrator.LastFallback);
            }
            else
            {
                k.TrySample(Keys.W,1000+previous,1000+end,out var f);k.TrySample(Keys.S,1000+previous,1000+end,out var b);
                legacy.Advance((end-previous)/1000.0,f.HeldFraction-b.HeldFraction,0,false,false,mouse,false);
            }
            trace[end]=precision?e.CurrentPose!.Value:ConvertLegacy(legacy.CurrentPose!.Value);
            previous=end;
        }
        return (precision?e.CurrentPose!.Value:ConvertLegacy(legacy.CurrentPose!.Value),trace);
    }
    private static RealtimePose ConvertLegacy(Legacy091.RealtimePose p)=>new(p.X,p.Y,p.Z,p.YawDegrees,p.PositionUncertaintyMeters,p.YawUncertaintyDegrees);
    private static (RealtimePose Pose, Dictionary<int,RealtimePose> Trace) ReplayReference(List<InputEvent> events,bool held)
    {
        var k=Keyboard(held);var e=new Legacy091.RealtimePoseEstimator();e.Anchor(Anchor());var trace=new Dictionary<int,RealtimePose>();int index=0;
        for(int t=1;t<=100000;t++)
        {
            int mouse=0;
            while(index<events.Count&&events[index].AtMs*10<=t)
            {
                var item=events[index++];if(item.Key!=Keys.None)k.Record(item.Key,item.Down,10000+item.AtMs*10);mouse+=item.Mouse;
            }
            k.TrySample(Keys.W,10000+t-1,10000+t,out var f);k.TrySample(Keys.S,10000+t-1,10000+t,out var b);
            e.Advance(.0001,f.HeldFraction-b.HeldFraction,0,false,false,mouse,false);
            if(t%10==0)trace[t/10]=ConvertLegacy(e.CurrentPose!.Value);
        }
        return (ConvertLegacy(e.CurrentPose!.Value),trace);
    }

    private static object BenchmarkHotPath()
    {
        var keyboard=Keyboard();
        for(int i=1;i<=512;i++)keyboard.Record(Keys.W,i%2==1,i);
        var yaw=new PrecisionYawTimeline();var integrator=new PrecisionMotionIntegrator(1000000);
        var e=Estimator();var times=new List<double>();long start=1000000;
        long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<600;frame++)
        {
            long end=start+50000;int total=0;
            for(int i=1;i<=400;i++) {int delta=i%2==0?1:-1;total+=delta;yaw.Record(start+i*125,delta,false);}
            long before=Stopwatch.GetTimestamp();
            bool ok=integrator.TryAdvance(e,keyboard,yaw,start,end,Keys.W,Keys.S,Keys.A,Keys.D,false,false,1,RealtimeMotionEvidence.Unknown,total,out _);
            var ms=(Stopwatch.GetTimestamp()-before)*1000.0/Stopwatch.Frequency;
            if(!ok)throw new InvalidOperationException("Stress repli : "+integrator.LastFallback);
            if(frame>=100)times.Add(ms);start=end;
        }
        times.Sort();return new{scenario="512 keyboard transitions retained + 8000 synthetic mouse samples/s",measuredTicks=times.Count,
            medianMs=times[times.Count/2],p95Ms=times[(int)(times.Count*.95)],maxMs=times[^1],
            allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore,gameCapture=false,includesGameRendering=false};
    }
    private static void Run(string name,Action test,ICollection<string> failures)
    { try {test();Console.WriteLine("OK  "+name);}catch(Exception ex){failures.Add(name+" — "+ex.Message);Console.Error.WriteLine("FAIL "+name);} }
}
