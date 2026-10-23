namespace RaidPilot.Overlay.Tests;

internal static class OverlayPreferencesStoreTests
{
    internal const int TestCount = 16;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("préférence de stream absente", MissingPreferenceDefaultsToVisible, failures);
        Run("préférence de stream corrompue", CorruptPreferenceDefaultsToVisible, failures);
        Run("propriété de stream absente", MissingPropertyDefaultsToVisible, failures);
        Run("préférence de stream explicite", LoadsExplicitVisibilityValues, failures);
        Run("sauvegarde atomique de la préférence de stream", SavesAndReplacesAtomically, failures);
        Run("affinité Windows de la préférence de stream", MapsVisibilityToDisplayAffinity, failures);
        Run("taille de carte absente utilise Auto", MissingMapScaleDefaultsToAuto, failures);
        Run("taille de carte persiste", SavesMapScale, failures);
        Run("taille conserve la préférence stream", MapScalePreservesStreamPreference, failures);
        Run("stream conserve la taille de carte", StreamPreferencePreservesMapScale, failures);
        Run("position absente utilise haut droite", MissingCornerDefaultsToTopRight, failures);
        Run("coin de carte persiste", SavesMapCorner, failures);
        Run("position libre persiste sans perdre les autres préférences", SavesFreeLayout, failures);
        Run("zoom global absent reste automatique", MissingMapZoomUsesDefault, failures);
        Run("zoom global conservé entre raids et les autres réglages", SavesMapZoomFactor, failures);
        Run("zoom global invalide refusé sans altérer les autres préférences", RejectsInvalidMapZoomFactor, failures);
    }

    private static void MissingPreferenceDefaultsToVisible()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "missing.json");
            Require(
                OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "une préférence absente doit laisser la carte visible");
        });
    }

    private static void CorruptPreferenceDefaultsToVisible()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "corrupt.json");
            File.WriteAllText(settingsPath, "{pas-du-json");
            Require(
                OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "une préférence corrompue doit laisser la carte visible");
        });
    }

    private static void MissingPropertyDefaultsToVisible()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "other-setting.json");
            File.WriteAllText(settingsPath, "{\"futureSetting\":false}");
            Require(
                OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "un ancien fichier sans la propriété doit laisser la carte visible");
        });
    }

    private static void LoadsExplicitVisibilityValues()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "settings.json");
            File.WriteAllText(settingsPath, "{\"visibleInCaptures\":false}");
            Require(
                !OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "la valeur false n'est pas relue");

            File.WriteAllText(settingsPath, "{\"visibleInCaptures\":true}");
            Require(
                OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "la valeur true n'est pas relue");
        });
    }

    private static void SavesAndReplacesAtomically()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "settings.json");
            Require(
                OverlayPreferencesStore.TrySaveVisibleInCaptures(false, settingsPath),
                "la première sauvegarde a échoué");
            Require(
                !OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "la première valeur sauvegardée est incorrecte");

            Require(
                OverlayPreferencesStore.TrySaveVisibleInCaptures(true, settingsPath),
                "le remplacement de la préférence a échoué");
            Require(
                OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "la valeur remplacée est incorrecte");
            Require(
                !Directory.EnumerateFiles(path, "*.tmp").Any(),
                "un fichier temporaire est resté après la sauvegarde");
        });
    }

    private static void MissingMapScaleDefaultsToAuto()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "missing-scale.json");
            Require(
                OverlayPreferencesStore.LoadMapScalePercent(settingsPath) is null,
                "une taille absente doit rester en mode Auto");
        });
    }

    private static void SavesMapScale()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "map-scale.json");
            Require(
                OverlayPreferencesStore.TrySaveMapScalePercent(70, settingsPath),
                "la taille 70% n'a pas été sauvegardée");
            Require(
                OverlayPreferencesStore.LoadMapScalePercent(settingsPath) == 70,
                "la taille 70% n'a pas été relue");

            Require(
                OverlayPreferencesStore.TrySaveMapScalePercent(null, settingsPath),
                "le retour au mode Auto a échoué");
            Require(
                OverlayPreferencesStore.LoadMapScalePercent(settingsPath) is null,
                "le mode Auto n'a pas été restauré");
        });
    }

    private static void MapScalePreservesStreamPreference()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "combined.json");
            Require(
                OverlayPreferencesStore.TrySaveVisibleInCaptures(false, settingsPath),
                "préférence stream initiale non sauvegardée");
            Require(
                OverlayPreferencesStore.TrySaveMapScalePercent(80, settingsPath),
                "taille de carte non sauvegardée");
            Require(
                !OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "la taille de carte a écrasé la préférence stream");
        });
    }

    private static void StreamPreferencePreservesMapScale()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "combined-reverse.json");
            Require(
                OverlayPreferencesStore.TrySaveMapScalePercent(65, settingsPath),
                "taille initiale non sauvegardée");
            Require(
                OverlayPreferencesStore.TrySaveVisibleInCaptures(false, settingsPath),
                "préférence stream non sauvegardée");
            Require(
                OverlayPreferencesStore.LoadMapScalePercent(settingsPath) == 65,
                "la préférence stream a écrasé la taille de carte");
        });
    }

    private static void MissingCornerDefaultsToTopRight()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "missing-corner.json");
            Require(
                OverlayPreferencesStore.LoadMapCorner(settingsPath) == OverlayCorner.TopRight,
                "le coin absent doit rester en haut à droite");
            Require(
                OverlayPreferencesStore.LoadFreeLayout(settingsPath) is null,
                "une position libre absente ne doit pas être inventée");
        });
    }

    private static void SavesMapCorner()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "corner.json");
            Require(
                OverlayPreferencesStore.TrySaveMapCorner(OverlayCorner.BottomLeft, settingsPath),
                "le coin bas gauche n'a pas été sauvegardé");
            Require(
                OverlayPreferencesStore.LoadMapCorner(settingsPath) == OverlayCorner.BottomLeft,
                "le coin bas gauche n'a pas été relu");
            Require(
                OverlayPreferencesStore.LoadFreeLayout(settingsPath) is null,
                "un préréglage de coin doit désactiver la position libre");
        });
    }

    private static void SavesFreeLayout()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "free-layout.json");
            var free = new OverlayFreeLayout(0.12, 0.18, 0.42, 0.46);
            Require(
                OverlayPreferencesStore.TrySaveVisibleInCaptures(false, settingsPath),
                "préférence stream initiale non sauvegardée");
            Require(
                OverlayPreferencesStore.TrySaveMapScalePercent(80, settingsPath),
                "taille initiale non sauvegardée");
            Require(
                OverlayPreferencesStore.TrySaveFreeLayout(free, settingsPath),
                "position libre non sauvegardée");
            Require(
                OverlayPreferencesStore.LoadFreeLayout(settingsPath) == free,
                "position libre non relue");
            Require(
                OverlayPreferencesStore.LoadMapScalePercent(settingsPath) == 80,
                "position libre a effacé la taille");
            Require(
                !OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "position libre a effacé la préférence stream");
        });
    }

    private static void MapsVisibilityToDisplayAffinity()
    {
        Require(
            OverlayCaptureVisibility.GetDisplayAffinity(visibleInCaptures: true)
                == NativeMethods.WdaNone,
            "la carte visible doit utiliser WDA_NONE");
        Require(
            OverlayCaptureVisibility.GetDisplayAffinity(visibleInCaptures: false)
                == NativeMethods.WdaExcludeFromCapture,
            "la carte masquée doit utiliser WDA_EXCLUDEFROMCAPTURE");
    }

    private static void InTemporaryDirectory(Action<string> action)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"RaidPilot-OverlayPreferencesTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void MissingMapZoomUsesDefault()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "missing-zoom.json");
            Require(OverlayPreferencesStore.LoadMapZoomFactor(settingsPath) is null,
                "aucun zoom ne doit être imposé au premier lancement");
        });
    }

    private static void SavesMapZoomFactor()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "zoom.json");
            Require(OverlayPreferencesStore.TrySaveMapZoomFactor(2.75, settingsPath),
                "enregistrement de zoom refusé");
            Require(OverlayPreferencesStore.LoadMapZoomFactor(settingsPath) is { } saved
                && Math.Abs(saved - 2.75) < 1e-12, "zoom incorrect après relecture");
            Require(OverlayPreferencesStore.TrySaveMapScalePercent(75, settingsPath)
                && OverlayPreferencesStore.TrySaveVisibleInCaptures(false, settingsPath),
                "modification des autres réglages échouée");
            Require(OverlayPreferencesStore.LoadMapZoomFactor(settingsPath) == 2.75
                && OverlayPreferencesStore.LoadMapScalePercent(settingsPath) == 75
                && !OverlayPreferencesStore.LoadVisibleInCaptures(settingsPath),
                "une autre préférence a écrasé la valeur zoom");
        });
    }

    private static void RejectsInvalidMapZoomFactor()
    {
        InTemporaryDirectory(path =>
        {
            var settingsPath = Path.Combine(path, "validation.json");
            Require(OverlayPreferencesStore.TrySaveMapZoomFactor(1.5, settingsPath),
                "valeur de base refusée");
            foreach (var invalid in new[] {
                double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                -1.0, 0, .19, 32.1, 10000
            })
                Require(!OverlayPreferencesStore.TrySaveMapZoomFactor(invalid, settingsPath),
                    "valeur hors bornes acceptée");
            Require(OverlayPreferencesStore.LoadMapZoomFactor(settingsPath) == 1.5,
                "la valeur invalide a écrasé la préférence existante");
            File.WriteAllText(settingsPath,
                "{\"mapZoomFactor\":99999,\"mapScalePercent\":70}");
            Require(OverlayPreferencesStore.LoadMapZoomFactor(settingsPath) is null
                && OverlayPreferencesStore.LoadMapScalePercent(settingsPath) == 70,
                "préférence invalide non ignorée ou réglage valide perdu");
        });
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
