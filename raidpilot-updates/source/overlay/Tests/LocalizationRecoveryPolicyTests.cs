namespace RaidPilot.Overlay.Tests;

internal static class LocalizationRecoveryPolicyTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("récupération localisation: seuil nominal", UsesNominalThresholds, failures);
        Run("récupération localisation: cadence moyenne", UsesMediumUncertaintyCadence, failures);
        Run("récupération localisation: cadence critique", UsesCriticalUncertaintyCadence, failures);
        Run("récupération localisation: pas de F8 si position fiable", RejectsReliablePose, failures);
        Run("récupération localisation: attend l'immobilité minimale", RequiresIdleWindow, failures);
        Run("récupération localisation: forte dérive accélère le recalage", AllowsFastRecoveryWhenUncertain, failures);
        Run("récupération localisation: santé rouge escalade prudemment", RedHealthEscalatesNativeAnchor, failures);
    }

    private static void UsesNominalThresholds()
    {
        Require(LocalizationRecoveryPolicy.AutomaticReanchorUncertaintyMeters == 2.25,
            "seuil d'incertitude automatique inattendu");
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(3.0), 4.5, 0.001,
            "cooldown nominal");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(3.0), 0.50, 0.001,
            "immobilité nominale");
    }

    private static void UsesMediumUncertaintyCadence()
    {
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(8.0), 2.75, 0.001,
            "cooldown moyen");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(8.0), 0.30, 0.001,
            "immobilité moyenne");
    }

    private static void UsesCriticalUncertaintyCadence()
    {
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(14.0), 2.0, 0.001,
            "cooldown critique");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(14.0), 0.22, 0.001,
            "immobilité critique");
    }

    private static void RejectsReliablePose()
    {
        Require(!LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(2.0, 10.0, 100.0),
            "une position fiable ne doit pas déclencher de screenshot automatique");
    }

    private static void RequiresIdleWindow()
    {
        Require(!LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(8.0, 0.20, 100.0),
            "le recalage ne doit pas partir avant une immobilité suffisante");
    }

    private static void AllowsFastRecoveryWhenUncertain()
    {
        Require(LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(14.0, 0.25, 2.1),
            "une forte dérive devrait autoriser une récupération accélérée");
    }

    private static void RedHealthEscalatesNativeAnchor()
    {
        Require(LocalizationRecoveryPolicy.ShouldEscalateFromLocalizationHealth(
                LocalizationHealthState.Red,
                uncertaintyMeters: 1.8,
                idleSeconds: 0.8,
                secondsSinceLastAttempt: 21.0),
            "une santé rouge ancienne devrait profiter d'une vraie pause");
        Require(!LocalizationRecoveryPolicy.ShouldEscalateFromLocalizationHealth(
                LocalizationHealthState.Orange,
                uncertaintyMeters: 3.0,
                idleSeconds: 10.0,
                secondsSinceLastAttempt: 100.0),
            "orange ne doit pas forcer la voie rouge");
        Require(!LocalizationRecoveryPolicy.ShouldEscalateFromLocalizationHealth(
                LocalizationHealthState.Red,
                uncertaintyMeters: 1.8,
                idleSeconds: 0.8,
                secondsSinceLastAttempt: 5.0),
            "le cooldown santé doit empêcher le spam F8");
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

    private static void RequireNear(double actual, double expected, double tolerance, string label)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{label}: attendu {expected}, reçu {actual}");
        }
    }
}