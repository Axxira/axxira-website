namespace RaidPilot.Overlay.Tests;

internal static class RealtimeTrackingPolicyTests
{
    internal const int TestCount = 9;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("Beacon: le sprint toggle ne repart pas après relâchement de W", () =>
        {
            var ended = RealtimeTrackingPolicy.ResolveModes(
                0, true, false, false, true, false, false);
            Require(!ended.Sprint && !ended.SprintLatched, "sprint conservé après relâchement");
            var resumed = RealtimeTrackingPolicy.ResolveModes(
                1, ended.SprintLatched, false, false, ended.SprintLatched, false, false);
            Require(!resumed.Sprint, "sprint fantôme réarmé à la prochaine marche");
        }, failures);
        Run("Beacon: le sprint annule ADS et la marche lente", () =>
        {
            var a = RealtimeTrackingPolicy.ResolveModes(1, true, true, true, true, true, true);
            Require(a.Sprint && !a.Slow && !a.Aiming && !a.WalkLatched && !a.AimLatched,
                "modes contradictoires pendant le sprint");
        }, failures);
        Run("Beacon: les modes normaux restent inchangés", () =>
        {
            var a = RealtimeTrackingPolicy.ResolveModes(1, false, true, true, false, true, true);
            Require(!a.Sprint && a.Slow && a.Aiming && a.WalkLatched && a.AimLatched,
                "marche et visée altérées sans sprint");
        }, failures);
        Run("Beacon: C relâché alterne la position accroupie", () =>
        {
            var a = RealtimeTrackingPolicy.UpdatePosture(default, true, false, false, false, false);
            var b = RealtimeTrackingPolicy.UpdatePosture(a, true, false, false, false, false);
            Require(a.Crouched && !a.Prone && !b.Crouched, "bascule accroupie incorrecte");
        }, failures);
        Run("Beacon: X puis C passe de couché à accroupi", () =>
        {
            var prone = RealtimeTrackingPolicy.UpdatePosture(default, false, false, true, false, false);
            var crouched = RealtimeTrackingPolicy.UpdatePosture(prone, true, false, false, false, false);
            Require(prone.Prone && !prone.Crouched && crouched.Crouched && !crouched.Prone,
                "changement de posture incohérent");
        }, failures);
        Run("Beacon: molette + C n'active pas l'accroupissement", () =>
        {
            var a = RealtimeTrackingPolicy.UpdatePosture(default, true, true, false, false, false);
            Require(!a.Crouched && !a.Prone, "réglage de hauteur pris pour un crouch");
        }, failures);
        Run("Beacon: sprint et saut remettent debout", () =>
        {
            var p = new RealtimeTrackingPosture(true, false);
            Require(RealtimeTrackingPolicy.UpdatePosture(p, false, false, false, false, true)
                == default(RealtimeTrackingPosture), "sprint n'a pas relevé le joueur");
            Require(RealtimeTrackingPolicy.UpdatePosture(p, false, false, false, true, false)
                == default(RealtimeTrackingPosture), "saut n'a pas relevé le joueur");
            Require(RealtimeTrackingPolicy.PostureSpeedScale(new(true, false)) == 0.55
                && RealtimeTrackingPolicy.PostureSpeedScale(new(false, true)) == 0.20
                && RealtimeTrackingPolicy.PostureSpeedScale(default) == 1.0,
                "facteurs de posture non alignés avec Overlay");
        }, failures);
        Run("Beacon: une scène 64×36 figée ne bloque pas la marche lente", () =>
        {
            Require(RealtimeTrackingPolicy.FilterAmbiguousStationaryEvidence(
                    RealtimeMotionEvidence.Stationary, true, false)
                == RealtimeMotionEvidence.Unknown, "faux arrêt de marche");
            Require(RealtimeTrackingPolicy.FilterAmbiguousStationaryEvidence(
                    RealtimeMotionEvidence.Stationary, true, true)
                == RealtimeMotionEvidence.Stationary, "détection d'obstacle sprint perdue");
            Require(RealtimeTrackingPolicy.FilterAmbiguousStationaryEvidence(
                    RealtimeMotionEvidence.Stationary, false, false)
                == RealtimeMotionEvidence.Stationary, "repos réel effacé");
        }, failures);
        Run("Beacon: une pause UI ne supprime pas silencieusement du temps", () =>
        {
            Require(Math.Abs(RealtimeTrackingPolicy.IntegratedSeconds(0.35) - 0.35) < 1e-12,
                "pause de 350 ms tronquée");
            Require(Math.Abs(RealtimeTrackingPolicy.IntegratedSeconds(1.05) - 0.75) < 1e-12
                && Math.Abs(RealtimeTrackingPolicy.UnobservedSeconds(1.05) - 0.30) < 1e-12,
                "intervalle sans suivi non identifié");
            Require(RealtimeTrackingPolicy.HasUnreliableGap(2.5)
                && !RealtimeTrackingPolicy.HasUnreliableGap(0.3),
                "longue interruption traitée comme une observation continue");
        }, failures);
    }

    private static void Run(string name, Action check, ICollection<string> failures)
    {
        try { check(); Console.WriteLine("OK  " + name); }
        catch (Exception error) { failures.Add(name + " : " + error.Message); }
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
