using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class PrecisionDriftAuditTests
{
    internal const int TestCount = 11;

    private static ScreenshotObservation Exact(double x = 0, double yaw = 0) =>
        new("synthetic-F8", x, 0, 0, 0, 0, 0, 1, yaw);
    private static RealtimePose Pred(double x = 0, double yaw = 0) =>
        new(x, 0, 0, yaw, 2, 2);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("F8 audit: un premier ancrage ne fabrique aucun résultat", () => {
            var audit = new PrecisionDriftAudit();
            Require(!audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true)
                && audit.Count == 0);
        }, failures);
        Run("F8 audit: mesure déplacement et cap avant recalage", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true);
            Require(audit.ObserveExactAnchor(Exact(0, 1), Pred(5, 359),
                30 * Stopwatch.Frequency, true));
            Require(audit.Last is {} s && Math.Abs(s.ErrorMeters - 5) < 1e-8
                && Math.Abs(s.HeadingErrorDegrees - 2) < 1e-8
                && Math.Abs(s.Seconds - 20) < 1e-6
                && s.PrecisionV2Enabled);
        }, failures);
        Run("F8 audit: mode V1 et V2 sont identifiés sans mélange", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, false);
            audit.ObserveExactAnchor(Exact(), Pred(3), 30 * Stopwatch.Frequency, false);
            audit.MarkInterrupted();
            audit.ObserveExactAnchor(Exact(), Pred(3), 40 * Stopwatch.Frequency, true);
            audit.ObserveExactAnchor(Exact(), Pred(5), 60 * Stopwatch.Frequency, true);
            Require(audit.Samples.Count == 2
                && !audit.Samples[0].PrecisionV2Enabled
                && audit.Samples[1].PrecisionV2Enabled);
        }, failures);
        Run("F8 audit: changement de mode invalide l'intervalle", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true);
            audit.MarkInterrupted();
            Require(!audit.ObserveExactAnchor(Exact(), Pred(5),
                30 * Stopwatch.Frequency, true) && audit.Count == 0);
            Require(audit.ObserveExactAnchor(Exact(), Pred(3),
                50 * Stopwatch.Frequency, true) && audit.Count == 1);
        }, failures);
        Run("F8 audit: une pause de suivi ne fait pas un échantillon", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, false);
            audit.MarkInterrupted();
            Require(!audit.ObserveExactAnchor(Exact(), Pred(3),
                30 * Stopwatch.Frequency, false));
        }, failures);
        Run("F8 audit: sans horodatage fiable le résultat est rejeté", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true);
            Require(!audit.ObserveExactAnchor(Exact(), Pred(5), 0, true));
            Require(!audit.ObserveExactAnchor(Exact(), Pred(3), 40 * Stopwatch.Frequency, true));
        }, failures);
        Run("F8 audit: les segments trop courts et trop longs sont refusés", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true);
            Require(!audit.ObserveExactAnchor(Exact(), Pred(1),
                11 * Stopwatch.Frequency, true));
            Require(!audit.ObserveExactAnchor(Exact(), Pred(2),
                1000 * Stopwatch.Frequency, true));
        }, failures);
        Run("F8 audit: pas de coordonnées absolues dans les statistiques", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(413), null, 10 * Stopwatch.Frequency, true);
            audit.ObserveExactAnchor(Exact(413), Pred(421), 30 * Stopwatch.Frequency, true);
            var summary = audit.Describe();
            Require(summary.Contains("8,0") || summary.Contains("8.0"));
            Require(!summary.Contains("413") && !summary.Contains("421"));
        }, failures);
        Run("F8 audit: médiane et P90 suivent l'ordre croissant", () => {
            var errors = new[] { 1d, 2d, 3d, 5d, 9d };
            Require(PrecisionDriftAudit.Median(errors) == 3
                && PrecisionDriftAudit.Percentile90(errors) == 9);
            Require(PrecisionDriftAudit.Median(new[] { 1d, 3d }) == 2);
        }, failures);
        Run("F8 audit: mémoire bornée à 32 intervalles", () => {
            var audit = new PrecisionDriftAudit();
            for (var i = 1; i <= 40; i++)
                audit.ObserveExactAnchor(Exact(), Pred(i), (long)(10 + i * 20) * Stopwatch.Frequency, true);
            Require(audit.Count == 32 && audit.Samples.Count == 32);
        }, failures);
        Run("F8 audit: un nouveau raid remet les statistiques à zéro", () => {
            var audit = new PrecisionDriftAudit();
            audit.ObserveExactAnchor(Exact(), null, 10 * Stopwatch.Frequency, true);
            audit.ObserveExactAnchor(Exact(), Pred(4), 30 * Stopwatch.Frequency, true);
            audit.Reset();
            Require(audit.Count == 0 && audit.Last is null && audit.Ignored == 0);
        }, failures);
    }

    private static void Run(string name, Action run, ICollection<string> failures)
    {
        try { run(); Console.WriteLine("OK  " + name); }
        catch (Exception error) { failures.Add(name + " : " + error.Message); }
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Assertion audit F8 échouée");
    }
}
