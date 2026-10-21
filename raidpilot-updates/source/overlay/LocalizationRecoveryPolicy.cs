namespace RaidPilot.Overlay;

/// <summary>
/// Politique de récupération de localisation commune à Overlay et Beacon.
/// Plus l'incertitude de position augmente, plus le prochain recalage exact
/// devient prioritaire. Les bornes restent assez larges pour éviter de spammer
/// F8 lorsque la position est déjà fiable.
/// </summary>
internal static class LocalizationRecoveryPolicy
{
    internal const double AutomaticReanchorUncertaintyMeters = 2.25;

    internal static double AutomaticReanchorCooldownSeconds(double uncertaintyMeters)
    {
        ValidateUncertainty(uncertaintyMeters);

        return uncertaintyMeters switch
        {
            >= 12.0 => 2.0,
            >= 7.0 => 2.75,
            >= 4.0 => 3.5,
            _ => 4.5,
        };
    }

    internal static double MinimumIdleSeconds(double uncertaintyMeters)
    {
        ValidateUncertainty(uncertaintyMeters);

        return uncertaintyMeters switch
        {
            >= 12.0 => 0.22,
            >= 7.0 => 0.30,
            >= 4.0 => 0.40,
            _ => 0.50,
        };
    }

    internal static bool ShouldAttemptAutomaticReanchor(
        double uncertaintyMeters,
        double idleSeconds,
        double secondsSinceLastAttempt)
    {
        ValidateUncertainty(uncertaintyMeters);
        if (!double.IsFinite(idleSeconds) || idleSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleSeconds));
        }
        if (double.IsNaN(secondsSinceLastAttempt) || secondsSinceLastAttempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondsSinceLastAttempt));
        }

        return uncertaintyMeters >= AutomaticReanchorUncertaintyMeters
            && idleSeconds >= MinimumIdleSeconds(uncertaintyMeters)
            && secondsSinceLastAttempt >= AutomaticReanchorCooldownSeconds(uncertaintyMeters);
    }

    internal static bool ShouldEscalateFromLocalizationHealth(
        bool localizationHealthIsRed,
        double uncertaintyMeters,
        double idleSeconds,
        double secondsSinceLastAttempt)
    {
        ValidateUncertainty(uncertaintyMeters);
        if (!double.IsFinite(idleSeconds) || idleSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleSeconds));
        }
        if (double.IsNaN(secondsSinceLastAttempt)
            || secondsSinceLastAttempt < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secondsSinceLastAttempt));
        }

        // Rouge signifie qu'aucune confirmation absolue récente n'a résisté
        // au temps et à l'incertitude. À partir de 1,75 m, profiter d'une
        // vraie pause du joueur pour obtenir un F8 exact évite d'attendre le
        // seuil nominal de 2,25 m. Le cooldown long interdit tout spam.
        return localizationHealthIsRed
            && uncertaintyMeters >= 1.75
            && idleSeconds >= 0.75
            && secondsSinceLastAttempt >= 20.0;
    }

    private static void ValidateUncertainty(double uncertaintyMeters)
    {
        if (!double.IsFinite(uncertaintyMeters) || uncertaintyMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uncertaintyMeters));
        }
    }
}