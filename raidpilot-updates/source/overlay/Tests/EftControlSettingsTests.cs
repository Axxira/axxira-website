using System.Windows.Forms;

namespace RaidPilot.Overlay.Tests;

internal static class EftControlSettingsTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("lecture des contrôles et sensibilités EFT", ParsesMovementAndMouseSettings, failures);
        Run("priorité aux touches principales EFT", KeepsFirstUsableAxisPair, failures);
        Run("modes maintenir et bascule des contrôles EFT", ParsesHoldAndTogglePressTypes, failures);
        Run("pressType inconnu conserve les valeurs sûres", KeepsSafeDefaultsForUnknownPressTypes, failures);
        Run("valeurs sûres pour réglages absents", UsesDefaultsForMissingOrInvalidValues, failures);
        Run("conversion des noms de touches Unity", MapsUnityKeyNamesToWinForms, failures);
        Run("lecture seule et JSON mal formé", ReadsWithoutWritingAndRejectsMalformedJson, failures);
        Run("combinaison ignorée au profit d'une touche simple", SkipsChordBindingVariant, failures);
    }

    private static void ParsesMovementAndMouseSettings()
    {
        const string json = """
            {
              "MouseSensitivity": 0.416,
              "MouseAimingSensitivity": 0.31,
              "axisBindings": [
                {
                  "axisName": "MoveX",
                  "pairs": [{
                    "positive": { "keyCode": ["RightArrow"] },
                    "negative": { "keyCode": ["Q"] }
                  }]
                },
                {
                  "axisName": "MoveY",
                  "pairs": [{
                    "positive": { "keyCode": ["Z"] },
                    "negative": { "keyCode": ["X"] }
                  }]
                }
              ],
              "keyBindings": [
                {
                  "keyName": "Sprint",
                  "variants": [{ "keyCode": ["RightShift"] }],
                  "pressType": "Press"
                },
                {
                  "keyName": "Walk",
                  "variants": [{ "keyCode": ["LeftControl"] }],
                  "pressType": "Continuous"
                },
                {
                  "keyName": "Aim",
                  "variants": [{ "keyCode": ["Mouse4"] }],
                  "pressType": "Continuous"
                },
                {
                  "keyName": "Duck",
                  "variants": [{ "keyCode": ["C"] }],
                  "pressType": "Release"
                },
                {
                  "keyName": "Prone",
                  "variants": [{ "keyCode": ["X"] }],
                  "pressType": "Press"
                },
                {
                  "keyName": "Jump",
                  "variants": [{ "keyCode": ["Space"] }],
                  "pressType": "Release"
                }
              ]
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings),
            "le JSON EFT valide a été rejeté");
        Near(settings.MouseSensitivity, 0.416, "sensibilité générale");
        Near(settings.MouseAimingSensitivity, 0.31, "sensibilité visée");
        Require(settings.MoveForward == Keys.Z, "MoveY positif devrait être Z");
        Require(settings.MoveBackward == Keys.X, "MoveY négatif devrait être X");
        Require(settings.MoveLeft == Keys.Q, "MoveX négatif devrait être Q");
        Require(settings.MoveRight == Keys.Right, "MoveX positif devrait être Flèche droite");
        Require(settings.Sprint == Keys.RShiftKey, "Sprint devrait être Maj droite");
        Require(settings.Walk == Keys.LControlKey, "Walk devrait être Ctrl gauche");
        Require(settings.AimKey == Keys.XButton2, "Aim devrait être Bouton souris 5");
        Require(settings.DuckKey == Keys.C, "Duck devrait être C");
        Require(settings.ProneKey == Keys.X, "Prone devrait être X");
        Require(settings.JumpKey == Keys.Space, "Jump devrait être Espace");
        Require(settings.SprintIsToggle, "Sprint Press devrait être en mode bascule");
        Require(!settings.WalkIsToggle, "Walk Continuous devrait être en mode maintenir");
        Require(!settings.AimIsToggle, "Aim Continuous devrait être en mode maintenir");
    }

    private static void KeepsFirstUsableAxisPair()
    {
        const string json = """
            {
              "axisBindings": [
                {
                  "axisName": "MoveX",
                  "pairs": [
                    {
                      "positive": { "keyCode": ["D"] },
                      "negative": { "keyCode": ["A"] }
                    },
                    {
                      "positive": { "keyCode": ["RightArrow"] },
                      "negative": { "keyCode": ["LeftArrow"] }
                    }
                  ]
                },
                {
                  "axisName": "MoveY",
                  "pairs": [
                    {
                      "positive": { "keyCode": [] },
                      "negative": { "keyCode": [] }
                    },
                    {
                      "positive": { "keyCode": ["UpArrow"] },
                      "negative": { "keyCode": ["DownArrow"] }
                    }
                  ]
                }
              ]
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings),
            "le JSON à deux paires a été rejeté");
        Require(settings.MoveLeft == Keys.A && settings.MoveRight == Keys.D,
            "la liaison MoveX secondaire a remplacé la liaison principale");
        Require(settings.MoveForward == Keys.Up && settings.MoveBackward == Keys.Down,
            "la première liaison MoveY utilisable n'a pas été choisie");
    }

    private static void ParsesHoldAndTogglePressTypes()
    {
        const string json = """
            {
              "keyBindings": [
                {
                  "keyName": "Aim",
                  "variants": [{ "keyCode": ["Mouse1"] }],
                  "pressType": "press"
                },
                {
                  "keyName": "Sprint",
                  "variants": [{ "keyCode": ["LeftShift"] }],
                  "pressType": "CONTINUOUS"
                },
                {
                  "keyName": "Walk",
                  "variants": [{ "keyCode": ["CapsLock"] }],
                  "pressType": "Press"
                }
              ]
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings),
            "le JSON des modes de touche a été rejeté");
        Require(settings.AimKey == Keys.RButton && settings.AimIsToggle,
            "Aim Press devrait basculer la visée");
        Require(settings.SprintKey == Keys.LShiftKey && !settings.SprintIsToggle,
            "Sprint Continuous devrait être actif seulement pendant le maintien");
        Require(settings.WalkKey == Keys.CapsLock && settings.WalkIsToggle,
            "Walk Press devrait basculer la marche");
    }

    private static void KeepsSafeDefaultsForUnknownPressTypes()
    {
        const string json = """
            {
              "keyBindings": [
                {
                  "keyName": "Aim",
                  "variants": [{ "keyCode": ["Mouse2"] }],
                  "pressType": "Release"
                },
                {
                  "keyName": "Sprint",
                  "variants": [{ "keyCode": ["Space"] }],
                  "pressType": 17
                },
                {
                  "keyName": "Walk",
                  "variants": [{ "keyCode": ["LeftAlt"] }]
                }
              ]
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings),
            "le JSON avec pressType non pris en charge a été rejeté");
        Require(settings.AimKey == Keys.MButton
            && settings.SprintKey == Keys.Space
            && settings.WalkKey == Keys.LMenu,
            "les touches valides ne devraient pas être perdues à cause de pressType");
        Require(settings.AimIsToggle == EftControlSettings.Defaults.AimIsToggle
            && settings.SprintIsToggle == EftControlSettings.Defaults.SprintIsToggle
            && settings.WalkIsToggle == EftControlSettings.Defaults.WalkIsToggle,
            "un pressType absent ou inconnu devrait conserver les valeurs sûres");
    }

    private static void UsesDefaultsForMissingOrInvalidValues()
    {
        const string json = """
            {
              "MouseSensitivity": -1,
              "MouseAimingSensitivity": "invalide",
              "axisBindings": [{
                "axisName": "MoveX",
                "pairs": [{
                  "positive": { "keyCode": ["touche inconnue"] },
                  "negative": { "keyCode": [] }
                }]
              }],
              "keyBindings": []
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings),
            "un document partiel valide a été rejeté");
        Require(settings == EftControlSettings.Defaults,
            "les valeurs invalides ou absentes n'ont pas reçu les valeurs par défaut");
        Require(settings.MoveForward == Keys.W
            && settings.MoveBackward == Keys.S
            && settings.MoveLeft == Keys.A
            && settings.MoveRight == Keys.D
            && settings.Sprint == Keys.LShiftKey
            && settings.Walk == Keys.CapsLock
            && settings.AimKey == Keys.RButton,
            "les contrôles WASD/Maj/Verr. maj./clic droit par défaut sont incorrects");
        Require(settings.AimIsToggle
            && settings.SprintIsToggle
            && settings.WalkIsToggle,
            "les modes Press EFT par défaut devraient être des bascules");
    }

    private static void MapsUnityKeyNamesToWinForms()
    {
        Require(EftControlSettings.TryMapKeyCode("Alpha7", out var digit) && digit == Keys.D7,
            "Alpha7 n'est pas converti en touche 7");
        Require(EftControlSettings.TryMapKeyCode("Keypad3", out var keypad) && keypad == Keys.NumPad3,
            "Keypad3 n'est pas converti en pavé numérique 3");
        Require(EftControlSettings.TryMapKeyCode("LeftShift", out var shift) && shift == Keys.LShiftKey,
            "LeftShift n'est pas converti en Maj gauche");
        Require(EftControlSettings.TryMapKeyCode("CapsLock", out var caps) && caps == Keys.CapsLock,
            "CapsLock n'est pas conservé");
        Require(!EftControlSettings.TryMapKeyCode("DefinitelyNotAKey", out _),
            "une touche inconnue ne doit pas être inventée");
    }

    private static void SkipsChordBindingVariant()
    {
        const string json = """
            {
              "keyBindings": [{
                "keyName": "Sprint",
                "variants": [
                  { "keyCode": ["LeftControl", "X"] },
                  { "keyCode": ["RightShift"] }
                ]
              }]
            }
            """;

        Require(EftControlSettings.TryParse(json, out var settings), "le JSON du chord a été rejeté");
        Require(settings.Sprint == Keys.RShiftKey,
            "un chord ne doit pas être réduit à sa première touche");
    }

    private static void ReadsWithoutWritingAndRejectsMalformedJson()
    {
        const string json = """
            {
              "MouseSensitivity": 0.75,
              "keyBindings": [
                { "keyName": "Sprint", "variants": [{ "keyCode": ["Space"] }] }
              ]
            }
            """;
        var root = Path.Combine(Path.GetTempPath(), $"RaidPilot-controls-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "Control.ini");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(path, json);
            var settings = EftControlSettings.Read(path);

            Near(settings.MouseSensitivity, 0.75, "sensibilité lue du fichier");
            Require(settings.Sprint == Keys.Space, "la touche Sprint du fichier n'a pas été lue");
            Require(File.ReadAllText(path) == json, "Control.ini a été modifié par le lecteur");
            Require(!EftControlSettings.TryParse("{ invalid", out var malformed),
                "le JSON mal formé a été accepté");
            Require(malformed == EftControlSettings.Defaults,
                "un échec de lecture ne retourne pas les valeurs sûres");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static void Near(double actual, double expected, string label, double tolerance = 0.000001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F3}, reçu {actual:F3}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
