namespace RaidPilot.Overlay.Tests;

internal static class VisualMotionGateTests
{
    internal const int TestCount = 14;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("mouvement visuel confirmé avec hystérésis", ConfirmsMovementWithHysteresis, failures);
        Run("animation locale du HUD ignorée comme déplacement", RejectsLocalizedHudMotion, failures);
        Run("mouvement réparti dans la scène confirmé", ConfirmsDistributedSceneMotion, failures);
        Run("immobilité refusée avant preuve de capture vivante", RefusesStationaryBeforeLiveCapture, failures);
        Run("immobilité confirmée après mouvement", ConfirmsStationaryAfterMovement, failures);
        Run("preuve ambiguë libère immédiatement l'immobilité", ReleasesStationaryOnAmbiguousEvidence, failures);
        Run("variation globale de luminosité non stationnaire", RejectsBrightnessShiftAsStationary, failures);
        Run("zone de capture large et bornée", CalculatesBoundedWideCaptureRegion, failures);
        Run("nouvel appui libère immédiatement l'arrêt", ReleasesStationaryForMovementInput, failures);
        Run("capture figée perd sa confiance", InvalidatesFrozenCaptureLiveness, failures);
        Run("odométrie publiée avec l'observation visuelle", PublishesEgomotionWithObservation, failures);
        Run("double preuve visuelle confirme l'arrêt plus vite", ConfirmsStationaryFasterWithReliableEgomotion, failures);
        Run("PNG encore en écriture repris hors UI", RetriesIncompletePng, failures);
        Run("réglage visuel NaN refusé", RejectsNonFiniteCrop, failures);
    }

    private static void ConfirmsMovementWithHysteresis()
    {
        using var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        var second = TexturedFrame(phase: 1);

        var initial = Observe(gate, first);
        var firstEvidence = Observe(gate, second);
        var confirmed = Observe(gate, first);

        Require(initial.State == VisualMotionState.Unknown, "la première trame devrait servir de référence");
        Require(firstEvidence.Evidence == VisualMotionState.Moving, "la variation devrait être détectée");
        Require(firstEvidence.State == VisualMotionState.Unknown, "une seule trame ne devrait pas confirmer le mouvement");
        Require(confirmed.State == VisualMotionState.Moving, "deux preuves devraient confirmer le mouvement");
        Require(confirmed.CaptureProvenLive, "la capture devrait être marquée vivante");
    }

    private static void RefusesStationaryBeforeLiveCapture()
    {
        using var gate = NewGate();
        var frame = TexturedFrame(phase: 0);

        VisualMotionObservation observation = default;
        for (var index = 0; index < 8; index++)
        {
            observation = Observe(gate, frame);
        }

        Require(observation.Evidence == VisualMotionState.Stationary, "les pixels identiques sont une preuve brute d'immobilité");
        Require(observation.State == VisualMotionState.Unknown, "une capture possiblement figée ne doit jamais bloquer le déplacement");
        Require(!observation.CaptureProvenLive, "la capture ne devrait pas être tenue pour vivante");
    }

    private static void RejectsLocalizedHudMotion()
    {
        using var gate = NewGate();
        var stable = TexturedFrame(phase: 0);
        var localAnimation = (byte[])stable.Clone();

        // Simule une animation vive dans une petite zone basse/centrale: à
        // l'échelle globale elle dépasse volontairement le seuil moyen qui,
        // seul, produisait auparavant un faux Moving.
        for (var y = 9; y < 12; y++)
        {
            for (var x = 6; x < 10; x++)
            {
                var index = (y * 16) + x;
                localAnimation[index] = (byte)(255 - localAnimation[index]);
            }
        }

        Observe(gate, stable);
        var animated = Observe(gate, localAnimation);
        var returned = Observe(gate, stable);

        Require(
            animated.MeanDifference >= 5.0,
            "la simulation locale doit dépasser l'ancien seuil global de mouvement");
        Require(animated.Evidence == VisualMotionState.Stationary, "la zone arme masquée devrait laisser le décor stationnaire");
        Require(returned.Evidence == VisualMotionState.Stationary, "le retour du HUD devrait laisser le décor stationnaire");
        Require(gate.State == VisualMotionState.Unknown, "l'animation locale ne doit pas qualifier la capture de vivante");
        Require(!gate.CaptureProvenLive, "une animation du HUD ne doit pas devenir une preuve de déplacement");

        using var liveGate = NewGate();
        Observe(liveGate, stable);
        Observe(liveGate, TexturedFrame(phase: 1));
        Observe(liveGate, stable);
        Require(liveGate.CaptureProvenLive, "précondition de capture vivante absente");
        VisualMotionObservation settled = default;
        for (var frame = 0; frame < 4; frame++)
        {
            settled = Observe(liveGate, frame % 2 == 0 ? localAnimation : stable);
        }
        Require(settled.State == VisualMotionState.Stationary,
            "une animation masquée ne devrait pas empêcher le repos automatique");
    }

    private static void ConfirmsDistributedSceneMotion()
    {
        using var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        var shifted = TexturedFrame(phase: 3);

        Observe(gate, first);
        var firstEvidence = Observe(gate, shifted);
        var confirmed = Observe(gate, first);

        Require(firstEvidence.Evidence == VisualMotionState.Moving, "la variation répartie devrait être reconnue");
        Require(confirmed.Evidence == VisualMotionState.Moving, "le mouvement réparti devrait persister");
        Require(confirmed.State == VisualMotionState.Moving, "deux trames réparties devraient confirmer Moving");
        Require(confirmed.CaptureProvenLive, "le mouvement de scène devrait qualifier la capture");
    }

    private static void ConfirmsStationaryAfterMovement()
    {
        using var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        var second = TexturedFrame(phase: 1);
        Observe(gate, first);
        Observe(gate, second);
        var moving = Observe(gate, first);
        Require(moving.State == VisualMotionState.Moving, "précondition de mouvement absente");

        VisualMotionObservation observation = default;
        for (var index = 0; index < 4; index++)
        {
            observation = Observe(gate, first);
        }

        Require(observation.Evidence == VisualMotionState.Stationary, "les trames identiques devraient être stationnaires");
        Require(observation.State == VisualMotionState.Stationary, "l'hystérésis devrait confirmer l'immobilité");
    }

    private static void ReleasesStationaryOnAmbiguousEvidence()
    {
        using var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        var second = TexturedFrame(phase: 1);
        Observe(gate, first);
        Observe(gate, second);
        Observe(gate, first);
        for (var index = 0; index < 4; index++)
        {
            Observe(gate, first);
        }
        Require(gate.State == VisualMotionState.Stationary, "précondition d'immobilité absente");

        var ambiguous = (byte[])first.Clone();
        for (var index = 0; index < ambiguous.Length; index += 10)
        {
            ambiguous[index] = (byte)Math.Clamp(ambiguous[index] + 12, 0, 255);
        }
        var observation = Observe(gate, ambiguous);

        Require(observation.Evidence == VisualMotionState.Unknown, "la variation devrait tomber dans la zone prudente");
        Require(observation.State == VisualMotionState.Unknown, "un doute doit immédiatement libérer Stationary");
    }

    private static void RejectsBrightnessShiftAsStationary()
    {
        using var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        Observe(gate, first);

        var brighter = first.Select(value => (byte)Math.Min(255, value + 8)).ToArray();
        var observation = Observe(gate, brighter);

        Require(Math.Abs(observation.BrightnessDelta) > 1.5, "le changement de luminosité n'a pas été mesuré");
        Require(observation.Evidence == VisualMotionState.Unknown, "un changement d'exposition ne doit pas prouver l'immobilité");
    }

    private static void CalculatesBoundedWideCaptureRegion()
    {
        var region = VisualMotionGate.CalculateCaptureRegion(1920, 1080);

        Require(region.Left >= 0 && region.Top >= 0, "origine négative");
        Require(region.Left + region.Width <= 1920, "la zone dépasse la largeur cliente");
        Require(region.Top + region.Height <= 1080, "la zone dépasse la hauteur cliente");
        Require(region.Width >= 1500 && region.Height >= 700, "la zone devrait couvrir une large portion du jeu");
    }

    private static void ReleasesStationaryForMovementInput()
    {
        using var gate = CreateStationaryLiveGate();

        gate.ReleaseStationary();

        Require(gate.State == VisualMotionState.Unknown, "un nouvel appui ne doit pas attendre la prochaine capture");
        Require(gate.CaptureProvenLive, "la capture peut rester qualifiée après un simple redémarrage");
    }

    private static void InvalidatesFrozenCaptureLiveness()
    {
        using var gate = CreateStationaryLiveGate();

        gate.InvalidateLiveness();

        Require(gate.State == VisualMotionState.Unknown, "la capture figée ne doit plus bloquer");
        Require(!gate.CaptureProvenLive, "une capture incohérente doit refaire ses preuves");
    }

    private static void PublishesEgomotionWithObservation()
    {
        const int width = 64;
        const int height = 36;
        using var gate = new VisualMotionGate(new VisualMotionGateSettings
        {
            SampleWidth = width,
            SampleHeight = height,
        });

        var first = EgomotionFrame(width, height, shiftX: 0);
        var shifted = EgomotionFrame(width, height, shiftX: -2);
        var priming = gate.ObserveLuminanceFrame(first, width, height);
        var tracked = gate.ObserveLuminanceFrame(shifted, width, height);

        Require(priming.Egomotion.State == VisualEgomotionState.Priming,
            "la première observation devrait amorcer l'odométrie");
        Require(tracked.Egomotion.State == VisualEgomotionState.Tracking,
            $"le garde visuel devrait publier le suivi, reçu {tracked.Egomotion.State}");
        Require(Math.Abs(tracked.Egomotion.SceneShiftX + 2) < 0.01,
            $"décalage publié inattendu: {tracked.Egomotion.SceneShiftX:F2}");
        Require(tracked.Egomotion.HorizontalCameraDirection == VisualHorizontalDirection.Right,
            "la direction caméra publiée est inversée");
    }

    private static void ConfirmsStationaryFasterWithReliableEgomotion()
    {
        const int width = 64;
        const int height = 36;
        using var gate = new VisualMotionGate(new VisualMotionGateSettings
        {
            SampleWidth = width,
            SampleHeight = height,
            MovingFramesRequired = 2,
            StationaryFramesRequired = 4,
        });

        var first = EgomotionFrame(width, height, shiftX: 0);
        var shifted = EgomotionFrame(width, height, shiftX: -2);
        gate.ObserveLuminanceFrame(first, width, height);
        gate.ObserveLuminanceFrame(shifted, width, height);
        var moving = gate.ObserveLuminanceFrame(first, width, height);
        Require(moving.State == VisualMotionState.Moving,
            "précondition de capture vivante en mouvement absente");

        var firstRest = gate.ObserveLuminanceFrame(first, width, height);
        var confirmed = gate.ObserveLuminanceFrame(first, width, height);

        Require(firstRest.Egomotion.State == VisualEgomotionState.Stationary,
            "le suivi de points devrait confirmer la scène immobile");
        Require(firstRest.State != VisualMotionState.Stationary,
            "une seule double preuve ne doit pas suffire au seuil de quatre");
        Require(confirmed.State == VisualMotionState.Stationary,
            "deux doubles preuves devraient confirmer l'arrêt");
    }

    private static void RetriesIncompletePng()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"raidpilot-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "capture.png");
        try
        {
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E]);
            var replacement = Task.Run(async () =>
            {
                await Task.Delay(90);
                using var bitmap = new Bitmap(128, 72);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.FromArgb(34, 82, 126));
                    graphics.FillRectangle(Brushes.White, 16, 12, 72, 38);
                    graphics.FillEllipse(Brushes.Black, 54, 20, 32, 32);
                }

                using var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllBytes(path, stream.ToArray());
            });

            using var gate = new VisualMotionGate();
            var frame = gate.CreateLuminanceFrameFromImageWithRetryAsync(
                    path,
                    CancellationToken.None,
                    maximumAttempts: 5,
                    initialRetryDelay: TimeSpan.FromMilliseconds(40))
                .GetAwaiter()
                .GetResult();
            replacement.GetAwaiter().GetResult();

            Require(frame is { Length: 64 * 36 },
                "le PNG complété pendant la fenêtre de retry devrait produire une trame");
            Require(frame!.Distinct().Count() > 1,
                "la trame issue du PNG ne devrait pas être uniforme");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void RejectsNonFiniteCrop()
    {
        try
        {
            using var _ = new VisualMotionGate(new VisualMotionGateSettings
            {
                CropLeftFraction = double.NaN,
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new InvalidOperationException("une zone NaN ne doit pas atteindre GDI");
    }

    private static VisualMotionGate CreateStationaryLiveGate()
    {
        var gate = NewGate();
        var first = TexturedFrame(phase: 0);
        var second = TexturedFrame(phase: 1);
        Observe(gate, first);
        Observe(gate, second);
        Observe(gate, first);
        for (var index = 0; index < 4; index++)
        {
            Observe(gate, first);
        }
        Require(gate.State == VisualMotionState.Stationary, "précondition d'immobilité absente");
        return gate;
    }

    private static VisualMotionGate NewGate()
    {
        return new VisualMotionGate(new VisualMotionGateSettings
        {
            SampleWidth = 16,
            SampleHeight = 12,
            MovingFramesRequired = 2,
            StationaryFramesRequired = 4,
        });
    }

    private static VisualMotionObservation Observe(VisualMotionGate gate, byte[] frame)
    {
        return gate.ObserveLuminanceFrame(frame, 16, 12);
    }

    private static byte[] TexturedFrame(int phase)
    {
        var frame = new byte[16 * 12];
        for (var y = 0; y < 12; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var shiftedX = (x + phase) % 16;
                frame[(y * 16) + x] = (byte)(((shiftedX * 37) + (y * 53) + ((x * y) * 11)) % 180 + 35);
            }
        }
        return frame;
    }

    private static byte[] EgomotionFrame(int width, int height, int shiftX)
    {
        var frame = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                unchecked
                {
                    var worldX = x - shiftX;
                    var hash = (uint)(worldX * 0x1f123bb5)
                        ^ (uint)(y * 0x5f356495)
                        ^ 0x6c8e9cf5u;
                    hash ^= hash >> 16;
                    hash *= 0x7feb352d;
                    hash ^= hash >> 15;
                    hash *= 0x846ca68b;
                    hash ^= hash >> 16;
                    frame[(y * width) + x] = (byte)(40 + (hash % 161));
                }
            }
        }
        return frame;
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
