namespace RaidPilot.Overlay.Tests;

internal static class VisualEgomotionEstimatorTests
{
    internal const int TestCount = 15;

    private const int Width = 96;
    private const int Height = 54;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("odométrie: première trame d'amorçage", PrimesOnFirstFrame, failures);
        Run("odométrie: décor gauche indique caméra droite", TracksLeftSceneShiftAsRightCameraMotion, failures);
        Run("odométrie: décor droit indique caméra gauche", TracksRightSceneShiftAsLeftCameraMotion, failures);
        Run("odométrie: scène immobile", RecognizesStationaryScene, failures);
        Run("odométrie: décalage vertical sans faux lacet", TracksVerticalShiftWithoutYaw, failures);
        Run("odométrie: exposition globale compensée", CompensatesGlobalBrightnessShift, failures);
        Run("odométrie: HUD et arme masqués", IgnoresMaskedHudAndWeaponAnimation, failures);
        Run("odométrie: objet mobile local rejeté par consensus", RejectsLocalizedForegroundOutliers, failures);
        Run("odométrie: mouvements contradictoires rejetés", RejectsIncoherentGlobalAnimation, failures);
        Run("odométrie: image indépendante rejetée", RejectsUnrelatedFrame, failures);
        Run("odométrie: image uniforme rejetée", RejectsTexturelessFrame, failures);
        Run("odométrie: redimensionnement réamorce", ReprimesAfterResize, failures);
        Run("odométrie: remise à zéro réamorce", ResetReprimesEstimator, failures);
        Run("odométrie: échantillon économique rafraîchit la référence", CheapSampleRefreshesReference, failures);
        Run("odométrie: entrées et réglages invalides refusés", RejectsInvalidInputsAndSettings, failures);
    }

    private static void PrimesOnFirstFrame()
    {
        var estimator = new VisualEgomotionEstimator();

        var estimate = estimator.Observe(Scene(), Width, Height);

        Require(estimate.State == VisualEgomotionState.Priming, "la première trame doit seulement amorcer le suivi");
        Require(!estimate.IsReliable, "une seule trame ne peut pas produire un mouvement fiable");
    }

    private static void TracksLeftSceneShiftAsRightCameraMotion()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(shiftX: -3), Width, Height);

        RequireReliableTracking(estimate);
        RequireNear(estimate.SceneShiftX, -3, 0.01, "décalage horizontal du décor");
        RequireNear(estimate.SceneShiftY, 0, 0.01, "décalage vertical du décor");
        Require(estimate.HorizontalCameraDirection == VisualHorizontalDirection.Right,
            "un décor qui glisse à gauche doit indiquer une caméra vers la droite");
        Require(estimate.EstimatedYawDeltaDegrees > 0, "le lacet vers la droite devrait être positif");
    }

    private static void TracksRightSceneShiftAsLeftCameraMotion()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(shiftX: 4), Width, Height);

        RequireReliableTracking(estimate);
        RequireNear(estimate.SceneShiftX, 4, 0.01, "décalage horizontal du décor");
        Require(estimate.HorizontalCameraDirection == VisualHorizontalDirection.Left,
            "un décor qui glisse à droite doit indiquer une caméra vers la gauche");
        Require(estimate.EstimatedYawDeltaDegrees < 0, "le lacet vers la gauche devrait être négatif");
    }

    private static void RecognizesStationaryScene()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(), Width, Height);

        Require(estimate.State == VisualEgomotionState.Stationary, "deux scènes identiques devraient être stationnaires");
        Require(estimate.IsReliable, "la texture distribuée devrait rendre l'arrêt fiable");
        RequireNear(estimate.SceneShiftX, 0, 0.01, "décalage horizontal stationnaire");
        Require(estimate.HorizontalCameraDirection == VisualHorizontalDirection.None,
            "une scène immobile ne doit pas inventer de direction");
    }

    private static void TracksVerticalShiftWithoutYaw()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(shiftY: 2), Width, Height);

        RequireReliableTracking(estimate);
        RequireNear(estimate.SceneShiftX, 0, 0.01, "le mouvement vertical ne devrait pas créer de déplacement horizontal");
        RequireNear(estimate.SceneShiftY, 2, 0.01, "décalage vertical");
        Require(estimate.HorizontalCameraDirection == VisualHorizontalDirection.None,
            "le mouvement vertical ne devrait pas créer un faux lacet");
        RequireNear(estimate.EstimatedYawDeltaDegrees, 0, 0.01, "lacet vertical");
    }

    private static void CompensatesGlobalBrightnessShift()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(brightnessOffset: 14), Width, Height);

        Require(estimate.State == VisualEgomotionState.Stationary,
            "une exposition uniforme ne devrait pas devenir un déplacement");
        RequireNear(estimate.SceneShiftX, 0, 0.01, "décalage causé par l'exposition");
        RequireNear(estimate.BrightnessDelta, 14, 0.01, "variation moyenne de luminosité");
    }

    private static void IgnoresMaskedHudAndWeaponAnimation()
    {
        var estimator = PrimedEstimator();
        var animated = Scene();

        // Panneaux latéraux et arme basse/centrale: exactement les régions qui
        // doivent rester hors du consensus de la caméra.
        Paint(animated, 0, 0, 8, Height, seed: 101);
        Paint(animated, Width - 8, 0, 8, Height, seed: 102);
        Paint(animated, 22, 36, 52, 18, seed: 103);

        var estimate = estimator.Observe(animated, Width, Height);

        Require(estimate.State == VisualEgomotionState.Stationary,
            "les animations dans les masques HUD/arme ne devraient pas déplacer la caméra");
        RequireNear(estimate.SceneShiftX, 0, 0.01, "faux mouvement du HUD");
    }

    private static void RejectsLocalizedForegroundOutliers()
    {
        var estimator = PrimedEstimator();
        var shifted = Scene(shiftX: -3);

        // Un joueur/feuillage traverse une partie du décor, tandis que la
        // majorité des tuiles conserve le même flux optique.
        Paint(shifted, 10, 12, 22, 18, seed: 211);

        var estimate = estimator.Observe(shifted, Width, Height);

        RequireReliableTracking(estimate);
        RequireNear(estimate.SceneShiftX, -3, 0.01, "le premier plan local a faussé le consensus");
        Require(estimate.InlierFeatureCount < estimate.TexturedFeatureCount,
            "le test doit réellement contenir des points aberrants");
    }

    private static void RejectsIncoherentGlobalAnimation()
    {
        var estimator = PrimedEstimator();
        var conflicting = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var shift = x < Width / 3
                    ? -3
                    : x < Width * 2 / 3
                        ? 0
                        : 3;
                conflicting[(y * Width) + x] = Texture(x - shift, y, seed: 1);
            }
        }

        var estimate = estimator.Observe(conflicting, Width, Height);

        Require(estimate.State == VisualEgomotionState.Unreliable,
            $"trois flux contradictoires ne doivent pas produire une direction de caméra "
            + $"(état={estimate.State}, appariés={estimate.MatchedFeatureCount}, "
            + $"inliers={estimate.InlierFeatureCount}, ratio={estimate.InlierRatio:F2})");
        Require(!estimate.IsReliable, "une animation globale incohérente devrait être rejetée");
    }

    private static void RejectsUnrelatedFrame()
    {
        var estimator = PrimedEstimator();

        var estimate = estimator.Observe(Scene(seed: 77), Width, Height);

        Require(estimate.State == VisualEgomotionState.Unreliable,
            "une nouvelle image sans correspondance ne doit pas être suivie");
        Require(estimate.InlierFeatureCount == 0, "aucun consensus ne devrait être annoncé");
    }

    private static void RejectsTexturelessFrame()
    {
        var estimator = new VisualEgomotionEstimator();
        var flat = Enumerable.Repeat((byte)90, Width * Height).ToArray();
        estimator.Observe(flat, Width, Height);

        var estimate = estimator.Observe(flat, Width, Height);

        Require(estimate.State == VisualEgomotionState.Unreliable,
            "une image uniforme ne permet aucune odométrie");
        Require(estimate.TexturedFeatureCount == 0, "une image uniforme ne devrait fournir aucun point");
    }

    private static void ReprimesAfterResize()
    {
        var estimator = PrimedEstimator();
        const int resizedWidth = 80;
        const int resizedHeight = 48;

        var estimate = estimator.Observe(Scene(width: resizedWidth, height: resizedHeight), resizedWidth, resizedHeight);

        Require(estimate.State == VisualEgomotionState.Priming,
            "un changement de résolution doit empêcher la comparaison de mémoires incompatibles");
    }

    private static void ResetReprimesEstimator()
    {
        var estimator = PrimedEstimator();
        estimator.Reset();

        var estimate = estimator.Observe(Scene(), Width, Height);

        Require(estimate.State == VisualEgomotionState.Priming, "Reset doit oublier la trame précédente");
    }

    private static void CheapSampleRefreshesReference()
    {
        var estimator = PrimedEstimator();
        var moved = Scene(shiftX: -3);

        var skipped = estimator.Observe(moved, Width, Height, estimateMotion: false);
        var resumed = estimator.Observe(moved, Width, Height);

        Require(skipped.State == VisualEgomotionState.Priming,
            "un échantillon économique ne doit pas annoncer un mouvement calculé");
        Require(resumed.State == VisualEgomotionState.Stationary,
            "la reprise doit comparer avec la référence fraîche, pas une ancienne image");
    }

    private static void RejectsInvalidInputsAndSettings()
    {
        var estimator = new VisualEgomotionEstimator();
        RequireThrows<ArgumentException>(
            () => estimator.Observe(new byte[10], 4, 4),
            "une taille incohérente devrait être refusée");
        RequireThrows<ArgumentOutOfRangeException>(
            () => _ = new VisualEgomotionEstimator(new VisualEgomotionSettings { PatchRadius = 4 }),
            "une signature dépassant 63 bits devrait être refusée");
        RequireThrows<ArgumentOutOfRangeException>(
            () => _ = new VisualEgomotionEstimator(new VisualEgomotionSettings { MinimumInlierRatio = double.NaN }),
            "un seuil NaN devrait être refusé");
    }

    private static VisualEgomotionEstimator PrimedEstimator()
    {
        var estimator = new VisualEgomotionEstimator();
        var priming = estimator.Observe(Scene(), Width, Height);
        Require(priming.State == VisualEgomotionState.Priming, "précondition d'amorçage absente");
        return estimator;
    }

    private static byte[] Scene(
        int shiftX = 0,
        int shiftY = 0,
        int brightnessOffset = 0,
        int seed = 1,
        int width = Width,
        int height = Height)
    {
        var frame = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = Texture(x - shiftX, y - shiftY, seed);
                frame[(y * width) + x] = (byte)Math.Clamp(value + brightnessOffset, 0, 255);
            }
        }
        return frame;
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
            return (byte)(40 + (hash % 161));
        }
    }

    private static void Paint(byte[] frame, int left, int top, int width, int height, int seed)
    {
        for (var y = top; y < Math.Min(top + height, Height); y++)
        {
            for (var x = left; x < Math.Min(left + width, Width); x++)
            {
                frame[(y * Width) + x] = Texture(x, y, seed);
            }
        }
    }

    private static void RequireReliableTracking(VisualEgomotionEstimate estimate)
    {
        Require(estimate.State == VisualEgomotionState.Tracking,
            $"le suivi devrait être fiable, état reçu: {estimate.State}");
        Require(estimate.IsReliable, "le résultat Tracking doit être fiable");
        Require(estimate.Confidence >= 0.55, $"confiance trop faible: {estimate.Confidence:F3}");
        Require(estimate.InlierFeatureCount >= 6, "pas assez de points cohérents");
        Require(estimate.ResidualPixels <= 0.01, $"résidu inattendu: {estimate.ResidualPixels:F3}");
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void RequireNear(double actual, double expected, double tolerance, string label)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label}: attendu {expected:F3}, reçu {actual:F3}");
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
