using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class VisualKeyframeRecognizerTests
{
    internal const int TestCount = 21;

    private const int Width = 64;
    private const int Height = 36;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("repères visuels: trois confirmations exigées", RequiresThreeConsecutiveMatches, failures);
        Run("repères visuels: luminosité globale ignorée", MatchesThroughBrightnessChange, failures);
        Run("repères visuels: petit décalage d'image absorbé", MatchesSmallImageShift, failures);
        Run("repères visuels: petite animation locale tolérée", ToleratesSmallLocalChange, failures);
        Run("repères visuels: HUD, barre basse et arme masqués", IgnoresMaskedRegions, failures);
        Run("repères visuels: image uniforme refusée", RejectsTexturelessFrames, failures);
        Run("repères visuels: image étrangère refusée", RejectsUnrelatedScene, failures);
        Run("repères visuels: carte différente refusée", RejectsDifferentMap, failures);
        Run("repères visuels: proximité spatiale exigée", RejectsDistantEstimatedPose, failures);
        Run("repères visuels: cap voisin exigé", RejectsDistantEstimatedYaw, failures);
        Run("repères visuels: meilleur résultat ambigu refusé", RejectsAmbiguousLookalikes, failures);
        Run("repères visuels: sosie hors rayon reste ambigu", RejectsLookalikeOutsidePoseGate, failures);
        Run("repères visuels: loop closure global exige forte incertitude", GlobalLoopClosureRequiresHighUncertainty, failures);
        Run("repères visuels: loop closure global exige cinq confirmations", GlobalLoopClosureRequiresFiveConfirmations, failures);
        Run("repères visuels: loop closure global refuse cap impossible", GlobalLoopClosureRejectsImpossibleYaw, failures);
        Run("repères visuels: preuves fraîches et espacées", RequiresFreshSpacedEvidence, failures);
        Run("repères visuels: commande annule la preuve partielle", CancelsPendingEvidence, failures);
        Run("repères visuels: cooldown après confirmation", AppliesConfirmationCooldown, failures);
        Run("repères visuels: nouvelle session réarme la séquence", ResetSessionRequiresFreshSequence, failures);
        Run("repères visuels: mémoire bornée à 64 repères par carte", BoundsKeyframesPerMap, failures);
        Run("repères visuels: mémoire bornée à 16 cartes et entrées validées", BoundsMapsAndValidatesInputs, failures);
    }

    private static void RequiresThreeConsecutiveMatches()
    {
        var recognizer = NewRecognizer();
        var exact = Pose(12, 2, -9, 358);
        Require(recognizer.AddExactKeyframe("woods", Scene(), Width, Height, exact, Ticks(0), out var id),
            "le repère texturé devrait être accepté");

        var first = recognizer.Observe("WOODS", Scene(), Width, Height, Pose(13, 2, -8, 2), Ticks(0.1));
        var second = recognizer.Observe("woods", Scene(), Width, Height, Pose(13, 2, -8, 2), Ticks(0.2));
        var third = recognizer.Observe("woods", Scene(), Width, Height, Pose(13, 2, -8, 2), Ticks(0.3));

        Require(first.State == VisualKeyframeRecognitionState.Candidate && first.ConsecutiveMatches == 1,
            "la première observation devrait rester candidate");
        Require(second.State == VisualKeyframeRecognitionState.Candidate && second.ConsecutiveMatches == 2,
            "la deuxième observation devrait rester candidate");
        Require(!first.ExactPose.HasValue && !second.ExactPose.HasValue,
            "une pose exacte ne doit pas fuiter avant confirmation");
        Require(third.IsConfirmed, "la troisième observation cohérente devrait confirmer le repère");
        Require(third.KeyframeId == id, "l'identité du repère confirmé est incorrecte");
        Require(third.ExactPose == exact with { YawDegrees = 358 }, "la pose exacte confirmée est incorrecte");
        Require(third.DescriptorDistance <= 0.001, "une trame identique devrait avoir une distance nulle");
    }

    private static void MatchesThroughBrightnessChange()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "shoreline", Scene(), Pose(0, 4, 0, 90));
        var brighter = Scene(brightnessOffset: 17);

        var result = Confirm(recognizer, "shoreline", brighter, Pose(1, 4, 1, 92));

        Require(result.IsConfirmed, "un changement global de luminosité ne devrait pas cacher le lieu");
        Require(result.DescriptorDistance <= 0.01,
            $"la signature Census devrait rester stable, distance={result.DescriptorDistance:F3}");
    }

    private static void MatchesSmallImageShift()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "customs", Scene(), Pose(4, 0, 8, 45));

        var result = Confirm(
            recognizer,
            "customs",
            Scene(shiftX: 2, shiftY: -1),
            Pose(5, 0, 9, 47));

        Require(result.IsConfirmed, "la recherche d'alignement devrait absorber deux pixels de décalage");
        Require(result.DescriptorDistance <= 0.01, "le meilleur alignement devrait retrouver la signature");
    }

    private static void ToleratesSmallLocalChange()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "interchange", Scene(), Pose(20, 1, -5, 180));
        var changed = Scene();
        Paint(changed, 8, 12, 6, 5, seed: 98);

        var result = Confirm(recognizer, "interchange", changed, Pose(19, 1, -4, 178));

        Require(result.IsConfirmed, "une petite animation locale ne devrait pas vaincre le consensus global");
        Require(result.DescriptorDistance <= 0.115, "la distance locale dépasse le seuil strict");
    }

    private static void IgnoresMaskedRegions()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "reserve", Scene(), Pose(-4, 8, 14, 270));
        var animated = Scene();

        Paint(animated, 0, 0, Width, 4, seed: 101);          // HUD haut
        Paint(animated, 0, Height - 3, Width, 3, seed: 102); // barre basse
        Paint(animated, 15, 23, 36, 13, seed: 103);          // arme

        var result = Confirm(recognizer, "reserve", animated, Pose(-5, 8, 13, 268));

        Require(result.IsConfirmed, "les régions masquées ne devraient pas influencer la reconnaissance");
        Require(result.DescriptorDistance <= 0.001, "les animations masquées devraient donner une distance nulle");
    }

    private static void RejectsTexturelessFrames()
    {
        var recognizer = NewRecognizer();
        var flat = Enumerable.Repeat((byte)90, Width * Height).ToArray();

        var added = recognizer.AddExactKeyframe("factory4_day", flat, Width, Height, Pose(0, 0, 0, 0), 0, out var id);

        Require(!added, "une image uniforme ne doit pas devenir un repère");
        Require(id == 0, "un repère refusé ne devrait pas recevoir d'identité");
        Require(recognizer.KeyframeCountForMap("factory4_day") == 0,
            "une image uniforme ne doit consommer aucune place mémoire");
    }

    private static void RejectsUnrelatedScene()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "lighthouse", Scene(seed: 1), Pose(0, 0, 0, 0));

        var result = recognizer.Observe("lighthouse", Scene(seed: 77), Width, Height, Pose(1, 0, 1, 1), Ticks(1));

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "une scène indépendante ne devrait pas créer de candidat");
    }

    private static void RejectsDifferentMap()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "woods", Scene(), Pose(0, 0, 0, 0));

        var result = recognizer.Observe("shoreline", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(1));

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "un repère d'une autre carte ne doit jamais être proposé");
    }

    private static void RejectsDistantEstimatedPose()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "streets", Scene(), Pose(0, 0, 0, 20));

        var result = recognizer.Observe("streets", Scene(), Width, Height, Pose(36, 0, 0, 20), Ticks(1));

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "un repère à plus de 35 m doit être éliminé avant la vision");
    }

    private static void RejectsDistantEstimatedYaw()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "groundzero", Scene(), Pose(0, 0, 0, 350));

        var acceptedWrap = recognizer.Observe(
            "groundzero", Scene(), Width, Height, Pose(0, 0, 0, 10), Ticks(1));
        var rejected = recognizer.Observe(
            "groundzero", Scene(), Width, Height, Pose(0, 0, 0, 30), Ticks(2));

        Require(acceptedWrap.State == VisualKeyframeRecognitionState.Candidate,
            "l'écart de cap doit fonctionner autour de 0/360°");
        Require(rejected.State == VisualKeyframeRecognitionState.NoMatch,
            "un écart de cap supérieur à 25° doit être refusé");
    }

    private static void RejectsAmbiguousLookalikes()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "labyrinth", Scene(), Pose(-10, 0, 0, 0), timestamp: 0);
        Add(recognizer, "labyrinth", Scene(), Pose(10, 0, 0, 0), timestamp: 1);

        var result = recognizer.Observe("labyrinth", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(1));

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "deux lieux visuellement identiques doivent être rejetés faute de marge");
    }

    private static void RejectsLookalikeOutsidePoseGate()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "woods", Scene(), Pose(0, 0, 0, 0), timestamp: 0);
        Add(recognizer, "woods", Scene(), Pose(80, 0, 0, 0), timestamp: 1);

        var result = recognizer.Observe(
            "woods",
            Scene(),
            Width,
            Height,
            Pose(1, 0, 0, 0),
            Ticks(0.1));

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "un sosie hors du rayon de pose doit encore annuler l'unicité visuelle");
    }

    private static void GlobalLoopClosureRequiresHighUncertainty()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "streets", Scene(seed: 31), Pose(0, 0, 0, 20));

        var confident = recognizer.Observe(
            "streets",
            Scene(seed: 31),
            Width,
            Height,
            Pose(90, 0, 0, 20),
            Ticks(0.1),
            estimatedPositionUncertaintyMeters: 3.0);
        Require(confident.State == VisualKeyframeRecognitionState.NoMatch,
            "une pose encore fiable ne doit jamais ignorer la barrière spatiale locale");

        recognizer.CancelPendingMatch();
        var uncertain = recognizer.Observe(
            "streets",
            Scene(seed: 31),
            Width,
            Height,
            Pose(90, 0, 0, 20),
            Ticks(0.2),
            estimatedPositionUncertaintyMeters: 12.0);
        Require(uncertain.State == VisualKeyframeRecognitionState.Candidate
            && uncertain.ConsecutiveMatches == 1,
            "une forte incertitude devrait autoriser un candidat global très distinctif");
    }

    private static void GlobalLoopClosureRequiresFiveConfirmations()
    {
        var recognizer = NewRecognizer();
        var exact = Pose(-12, 1, 44, 275);
        Add(recognizer, "lighthouse", Scene(seed: 42), exact);

        VisualKeyframeRecognition result = default;
        for (var index = 1; index <= 5; index++)
        {
            result = recognizer.Observe(
                "lighthouse",
                Scene(seed: 42),
                Width,
                Height,
                Pose(75, 1, -30, 280),
                Ticks(index * 0.1),
                estimatedPositionUncertaintyMeters: 14.0);

            if (index < 5)
            {
                Require(!result.IsConfirmed && result.ConsecutiveMatches == index,
                    $"le loop closure global ne doit pas confirmer à la preuve {index}");
            }
        }

        Require(result.IsConfirmed,
            "cinq preuves globales cohérentes devraient fermer la boucle");
        Require(result.ExactPose == exact,
            "le loop closure global doit restaurer la pose exacte mémorisée");
    }

    private static void GlobalLoopClosureRejectsImpossibleYaw()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "reserve", Scene(seed: 53), Pose(0, 0, 0, 10));

        var result = recognizer.Observe(
            "reserve",
            Scene(seed: 53),
            Width,
            Height,
            Pose(80, 0, 0, 130),
            Ticks(0.1),
            estimatedPositionUncertaintyMeters: 20.0);

        Require(result.State == VisualKeyframeRecognitionState.NoMatch,
            "un loop closure global ne doit pas contourner un écart de cap impossible");
    }

    private static void RequiresFreshSpacedEvidence()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "customs", Scene(), Pose(0, 0, 0, 0));

        var first = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.10));
        var duplicate = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.12));
        var second = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.20));
        var stale = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.80));
        var freshSecond = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.90));
        var confirmed = recognizer.Observe(
            "customs", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(1.00));

        Require(first.ConsecutiveMatches == 1 && duplicate.ConsecutiveMatches == 1,
            "deux lectures trop rapprochées ne doivent compter qu'une preuve");
        Require(second.ConsecutiveMatches == 2,
            "une preuve fraîche suffisamment espacée devrait compter");
        Require(stale.ConsecutiveMatches == 1,
            "un trou supérieur au maximum doit recommencer la séquence");
        Require(freshSecond.ConsecutiveMatches == 2 && confirmed.IsConfirmed,
            "trois nouvelles preuves bornées devraient confirmer le repère");
    }

    private static void CancelsPendingEvidence()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "reserve", Scene(), Pose(0, 0, 0, 0));
        _ = recognizer.Observe(
            "reserve", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.1));
        _ = recognizer.Observe(
            "reserve", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.2));

        recognizer.CancelPendingMatch();
        var afterMovement = recognizer.Observe(
            "reserve", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.3));

        Require(afterMovement.State == VisualKeyframeRecognitionState.Candidate
            && afterMovement.ConsecutiveMatches == 1,
            "une commande de mouvement doit imposer trois nouvelles preuves");
    }

    private static void AppliesConfirmationCooldown()
    {
        var recognizer = new VisualKeyframeRecognizer(new VisualKeyframeRecognizerSettings
        {
            ConfirmationCooldown = TimeSpan.FromSeconds(8),
        });
        Add(recognizer, "woods", Scene(), Pose(0, 0, 0, 0));

        _ = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.1));
        _ = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.2));
        var confirmed = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.3));
        var during = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(7));
        var after = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(8.4));

        Require(confirmed.IsConfirmed, "précondition: une confirmation est requise");
        Require(during.State == VisualKeyframeRecognitionState.Cooldown,
            "aucun nouveau recalage ne doit partir pendant le cooldown");
        Require(after.State == VisualKeyframeRecognitionState.Candidate && after.ConsecutiveMatches == 1,
            "après cooldown, une nouvelle séquence complète doit commencer");
    }

    private static void ResetSessionRequiresFreshSequence()
    {
        var recognizer = NewRecognizer();
        Add(recognizer, "woods", Scene(), Pose(0, 0, 0, 0));
        _ = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.1));
        _ = recognizer.Observe("woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.2));

        recognizer.ResetSession();
        var afterReset = recognizer.Observe(
            "woods", Scene(), Width, Height, Pose(0, 0, 0, 0), Ticks(0.1));

        Require(afterReset.State == VisualKeyframeRecognitionState.Candidate
            && afterReset.ConsecutiveMatches == 1,
            "ResetSession doit oublier les confirmations partielles mais garder les repères");
        Require(recognizer.KeyframeCountForMap("woods") == 1,
            "ResetSession ne doit pas effacer l'apprentissage visuel en mémoire");
    }

    private static void BoundsKeyframesPerMap()
    {
        var recognizer = NewRecognizer();
        for (var index = 0; index < 65; index++)
        {
            var added = recognizer.AddExactKeyframe(
                "woods",
                Scene(seed: index + 1),
                Width,
                Height,
                Pose(index * 3.0, 0, 0, 0),
                index,
                out _);
            Require(added, $"le repère texturé {index} devrait être accepté");
        }

        Require(recognizer.KeyframeCountForMap("woods") == 64,
            "la carte doit conserver au maximum 64 signatures");
    }

    private static void BoundsMapsAndValidatesInputs()
    {
        var recognizer = NewRecognizer();
        for (var index = 0; index < 17; index++)
        {
            Add(recognizer, $"map-{index}", Scene(seed: index + 1), Pose(0, 0, 0, 0), index);
        }

        Require(recognizer.MapCount == 16, "la mémoire doit conserver au maximum 16 cartes");
        Require(recognizer.KeyframeCountForMap("map-0") == 0,
            "la carte la moins récemment utilisée devrait être évincée");
        Require(recognizer.KeyframeCountForMap("map-16") == 1,
            "la carte la plus récente devrait être présente");

        RequireThrows<ArgumentException>(
            () => recognizer.Observe(" ", Scene(), Width, Height, Pose(0, 0, 0, 0), 0),
            "un identifiant de carte vide doit être refusé");
        RequireThrows<ArgumentException>(
            () => recognizer.Observe("woods", new byte[10], Width, Height, Pose(0, 0, 0, 0), 0),
            "une taille de trame incohérente doit être refusée");
        RequireThrows<ArgumentException>(
            () => recognizer.Observe("woods", Scene(), Width, Height, Pose(double.NaN, 0, 0, 0), 0),
            "une pose non finie doit être refusée");
        RequireThrows<ArgumentOutOfRangeException>(
            () => _ = new VisualKeyframeRecognizer(new VisualKeyframeRecognizerSettings
            {
                MaximumKeyframesPerMap = 65,
            }),
            "les limites de mémoire ne doivent pas pouvoir dépasser le contrat");
    }

    private static VisualKeyframeRecognizer NewRecognizer() => new(
        new VisualKeyframeRecognizerSettings
        {
            ConfirmationCooldown = TimeSpan.Zero,
        });

    private static VisualKeyframeRecognition Confirm(
        VisualKeyframeRecognizer recognizer,
        string mapId,
        byte[] frame,
        VisualKeyframePose estimatedPose)
    {
        _ = recognizer.Observe(mapId, frame, Width, Height, estimatedPose, Ticks(0.1));
        _ = recognizer.Observe(mapId, frame, Width, Height, estimatedPose, Ticks(0.2));
        return recognizer.Observe(mapId, frame, Width, Height, estimatedPose, Ticks(0.3));
    }

    private static void Add(
        VisualKeyframeRecognizer recognizer,
        string mapId,
        byte[] frame,
        VisualKeyframePose pose,
        long timestamp = 0)
    {
        Require(recognizer.AddExactKeyframe(mapId, frame, Width, Height, pose, timestamp, out _),
            "le repère synthétique devrait être accepté");
    }

    private static VisualKeyframePose Pose(double x, double y, double z, double yaw) => new(x, y, z, yaw);

    private static long Ticks(double seconds) => (long)Math.Round(seconds * Stopwatch.Frequency);

    private static byte[] Scene(
        int shiftX = 0,
        int shiftY = 0,
        int brightnessOffset = 0,
        int seed = 1)
    {
        var frame = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var value = Texture(x - shiftX, y - shiftY, seed);
                frame[(y * Width) + x] = (byte)Math.Clamp(value + brightnessOffset, 0, 255);
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
            return (byte)(35 + (hash % 181));
        }
    }

    private static void Paint(byte[] frame, int left, int top, int width, int height, int seed)
    {
        for (var y = Math.Max(0, top); y < Math.Min(top + height, Height); y++)
        {
            for (var x = Math.Max(0, left); x < Math.Min(left + width, Width); x++)
            {
                frame[(y * Width) + x] = Texture(x, y, seed);
            }
        }
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
}
