namespace RaidPilot.Overlay.Tests;

internal static class LocalizationRecoveryPolicyTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("récupération localisation: seuil nominal", UsesNominalThresholds, failures);
        Run("récupération localisation: cadence moyenne", UsesMediumUncertaintyCadence, failures);
        Run("récupération localisation: cadence critique", UsesCriticalUncertaintyCadence, failures);
        Run("récupération localisation: pas de F8 si position fiable", RejectsReliablePose, failures);
        Run("récupération localisation: attend l'immobilité minimale", RequiresIdleWindow, failures);
        Run("récupération localisation: forte dérive accélère le recalage", AllowsFastRecoveryWhenUncertain, failures);
    }

    private static void UsesNominalThresholds()
    {
        Require(LocalizationRecoveryPolicy.AutomaticReanchorUncertaintyMeters == 3.5,
            "seuil d'incertitude automatique inattendu");
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(4.0), 8.0, 0.001,
            "cooldown nominal");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(4.0), 0.90, 0.001,
            "immobilité nominale");
    }

    private static void UsesMediumUncertaintyCadence()
    {
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(8.0), 6.0, 0.001,
            "cooldown moyen");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(8.0), 0.70, 0.001,
            "immobilité moyenne");
    }

    private static void UsesCriticalUncertaintyCadence()
    {
        RequireNear(LocalizationRecoveryPolicy.AutomaticReanchorCooldownSeconds(14.0), 4.5, 0.001,
            "cooldown critique");
        RequireNear(LocalizationRecoveryPolicy.MinimumIdleSeconds(14.0), 0.55, 0.001,
            "immobilité critique");
    }

    private static void RejectsReliablePose()
    {
        Require(!LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(3.0, 10.0, 100.0),
            "une position fiable ne doit pas déclencher de screenshot automatique");
    }

    private static void RequiresIdleWindow()
    {
        Require(!LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(8.0, 0.4, 100.0),
            "le recalage ne doit pas partir avant une immobilité suffisante");
    }

    private static void AllowsFastRecoveryWhenUncertain()
    {
        Require(LocalizationRecoveryPolicy.ShouldAttemptAutomaticReanchor(14.0, 0.6, 4.6),
            "une forte dérive devrait autoriser une récupération accélérée");
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
