using System.Text.Json;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// Movement and mouse settings read from Escape from Tarkov's Control.ini.
/// Missing or invalid individual values fall back to the normal EFT controls.
/// </summary>
public sealed record EftControlSettings(
    double MouseSensitivity,
    double MouseAimingSensitivity,
    Keys MoveForward,
    Keys MoveBackward,
    Keys MoveLeft,
    Keys MoveRight,
    Keys Sprint,
    Keys Walk,
    Keys Aim,
    Keys Duck,
    Keys Prone,
    Keys Jump,
    bool AimIsToggle,
    bool SprintIsToggle,
    bool WalkIsToggle)
{
    private const long MaximumControlFileBytes = 1024 * 1024;

    public const double DefaultMouseSensitivity = 0.5;
    public const double DefaultMouseAimingSensitivity = 0.5;

    public static EftControlSettings Defaults { get; } = new(
        DefaultMouseSensitivity,
        DefaultMouseAimingSensitivity,
        Keys.W,
        Keys.S,
        Keys.A,
        Keys.D,
        Keys.LShiftKey,
        Keys.CapsLock,
        Keys.RButton,
        Keys.C,
        Keys.X,
        Keys.Space,
        AimIsToggle: true,
        SprintIsToggle: true,
        WalkIsToggle: true);

    public static string DefaultControlIniPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Battlestate Games",
        "Escape from Tarkov",
        "Settings",
        "Control.ini");

    /// <summary>
    /// Reads the settings without locking or modifying Control.ini. A missing,
    /// inaccessible, oversized, or malformed file produces safe defaults.
    /// </summary>
    public static EftControlSettings Read(string? controlIniPath = null)
    {
        var path = string.IsNullOrWhiteSpace(controlIniPath)
            ? DefaultControlIniPath
            : controlIniPath;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > MaximumControlFileBytes)
            {
                return Defaults;
            }

            using var document = JsonDocument.Parse(stream, JsonOptions);
            return ReadDocument(document.RootElement);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return Defaults;
        }
    }

    /// <summary>
    /// Parses Control.ini content in memory. Structurally valid JSON returns
    /// true even when some settings are absent; those settings use defaults.
    /// </summary>
    public static bool TryParse(string? json, out EftControlSettings settings)
    {
        settings = Defaults;
        if (string.IsNullOrWhiteSpace(json)
            || json.Length > MaximumControlFileBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            settings = ReadDocument(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Maps Unity/EFT key names to their WinForms virtual-key equivalent.
    /// </summary>
    public static bool TryMapKeyCode(string? eftKeyCode, out Keys key)
    {
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(eftKeyCode))
        {
            return false;
        }

        var name = eftKeyCode.Trim();
        if (UnityKeyAliases.TryGetValue(name, out key))
        {
            return true;
        }

        if (name.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase)
            && name.Length == 6
            && name[5] is >= '0' and <= '9')
        {
            key = Keys.D0 + (name[5] - '0');
            return true;
        }

        if (name.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = name[6..];
            if (suffix.Length == 1 && suffix[0] is >= '0' and <= '9')
            {
                key = Keys.NumPad0 + (suffix[0] - '0');
                return true;
            }

            if (KeypadAliases.TryGetValue(suffix, out key))
            {
                return true;
            }
        }

        if (Enum.TryParse<Keys>(name, ignoreCase: true, out var parsed)
            && parsed != Keys.None
            && Enum.IsDefined(parsed))
        {
            key = parsed;
            return true;
        }

        return false;
    }

    // Convenience aliases for consumers that use "key" terminology.
    public Keys ForwardKey => MoveForward;
    public Keys BackwardKey => MoveBackward;
    public Keys LeftKey => MoveLeft;
    public Keys RightKey => MoveRight;
    public Keys SprintKey => Sprint;
    public Keys WalkKey => Walk;
    public Keys AimKey => Aim;
    public Keys DuckKey => Duck;
    public Keys ProneKey => Prone;
    public Keys JumpKey => Jump;

    private static JsonDocumentOptions JsonOptions => new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    private static EftControlSettings ReadDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Defaults;
        }

        var mouseSensitivity = ReadFiniteNonNegativeDouble(
            root,
            "MouseSensitivity",
            Defaults.MouseSensitivity);
        var mouseAimingSensitivity = ReadFiniteNonNegativeDouble(
            root,
            "MouseAimingSensitivity",
            Defaults.MouseAimingSensitivity);

        var forward = Defaults.MoveForward;
        var backward = Defaults.MoveBackward;
        var left = Defaults.MoveLeft;
        var right = Defaults.MoveRight;
        var sprint = Defaults.Sprint;
        var walk = Defaults.Walk;
        var aim = Defaults.Aim;
        var duck = Defaults.Duck;
        var prone = Defaults.Prone;
        var jump = Defaults.Jump;
        var aimIsToggle = Defaults.AimIsToggle;
        var sprintIsToggle = Defaults.SprintIsToggle;
        var walkIsToggle = Defaults.WalkIsToggle;

        if (TryGetProperty(root, "axisBindings", out var axes)
            && axes.ValueKind == JsonValueKind.Array)
        {
            foreach (var axis in axes.EnumerateArray())
            {
                if (!TryReadString(axis, "axisName", out var axisName)
                    || !TryGetProperty(axis, "pairs", out var pairs)
                    || pairs.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var pair in pairs.EnumerateArray())
                {
                    var foundKey = false;
                    if (string.Equals(axisName, "MoveX", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadDirectionKey(pair, "negative", out var negative))
                        {
                            left = negative;
                            foundKey = true;
                        }
                        if (TryReadDirectionKey(pair, "positive", out var positive))
                        {
                            right = positive;
                            foundKey = true;
                        }
                    }
                    else if (string.Equals(axisName, "MoveY", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadDirectionKey(pair, "negative", out var negative))
                        {
                            backward = negative;
                            foundKey = true;
                        }
                        if (TryReadDirectionKey(pair, "positive", out var positive))
                        {
                            forward = positive;
                            foundKey = true;
                        }
                    }

                    // The first usable pair is the primary keyboard binding.
                    if (foundKey)
                    {
                        break;
                    }
                }
            }
        }

        if (TryGetProperty(root, "keyBindings", out var bindings)
            && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var binding in bindings.EnumerateArray())
            {
                if (!TryReadString(binding, "keyName", out var keyName))
                {
                    continue;
                }

                if (string.Equals(keyName, "Sprint", StringComparison.OrdinalIgnoreCase))
                {
                    sprint = ReadBindingKey(binding, sprint);
                    sprintIsToggle = ReadBindingIsToggle(binding, sprintIsToggle);
                }
                else if (string.Equals(keyName, "Walk", StringComparison.OrdinalIgnoreCase))
                {
                    walk = ReadBindingKey(binding, walk);
                    walkIsToggle = ReadBindingIsToggle(binding, walkIsToggle);
                }
                else if (string.Equals(keyName, "Aim", StringComparison.OrdinalIgnoreCase))
                {
                    aim = ReadBindingKey(binding, aim);
                    aimIsToggle = ReadBindingIsToggle(binding, aimIsToggle);
                }
                else if (string.Equals(keyName, "Duck", StringComparison.OrdinalIgnoreCase))
                {
                    duck = ReadBindingKey(binding, duck);
                }
                else if (string.Equals(keyName, "Prone", StringComparison.OrdinalIgnoreCase))
                {
                    prone = ReadBindingKey(binding, prone);
                }
                else if (string.Equals(keyName, "Jump", StringComparison.OrdinalIgnoreCase))
                {
                    jump = ReadBindingKey(binding, jump);
                }
            }
        }

        return new EftControlSettings(
            mouseSensitivity,
            mouseAimingSensitivity,
            forward,
            backward,
            left,
            right,
            sprint,
            walk,
            aim,
            duck,
            prone,
            jump,
            aimIsToggle,
            sprintIsToggle,
            walkIsToggle);
    }

    private static bool TryReadDirectionKey(JsonElement pair, string direction, out Keys key)
    {
        key = Keys.None;
        return TryGetProperty(pair, direction, out var binding)
            && TryReadKeyCodes(binding, out key);
    }

    private static Keys ReadBindingKey(JsonElement binding, Keys fallback)
    {
        if (!TryGetProperty(binding, "variants", out var variants)
            || variants.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }

        foreach (var variant in variants.EnumerateArray())
        {
            if (TryReadKeyCodes(variant, out var result))
            {
                return result;
            }
        }

        return fallback;
    }

    /// <summary>
    /// EFT uses Press for controls whose state is toggled by one key press and
    /// Continuous for controls that remain active only while the key is held.
    /// Unknown values retain the normal EFT default for that action.
    /// </summary>
    private static bool ReadBindingIsToggle(JsonElement binding, bool fallback)
    {
        if (!TryReadString(binding, "pressType", out var pressType))
        {
            return fallback;
        }

        if (string.Equals(pressType, "Press", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(pressType, "Continuous", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fallback;
    }

    private static bool TryReadKeyCodes(JsonElement binding, out Keys key)
    {
        key = Keys.None;
        if (!TryGetProperty(binding, "keyCode", out var keyCodes)
            || keyCodes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var mappedKeys = new List<Keys>();
        foreach (var value in keyCodes.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String
                && TryMapKeyCode(value.GetString(), out var mappedKey))
            {
                mappedKeys.Add(mappedKey);
            }
        }

        // Le suivi de mouvement échantillonne une touche physique. Prendre la
        // première touche d'un chord (ex. Ctrl+X) inventerait des appuis; on
        // utilise plutôt la prochaine variante simple ou le défaut sûr.
        if (mappedKeys.Count != 1)
        {
            return false;
        }

        key = mappedKeys[0];
        return true;
    }

    private static double ReadFiniteNonNegativeDouble(
        JsonElement root,
        string propertyName,
        double fallback)
    {
        if (TryGetProperty(root, propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var parsed)
            && double.IsFinite(parsed)
            && parsed >= 0)
        {
            return parsed;
        }

        return fallback;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetProperty(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!.Trim();
        return true;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        property = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        return false;
    }

    private static readonly IReadOnlyDictionary<string, Keys> UnityKeyAliases =
        new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            ["LeftShift"] = Keys.LShiftKey,
            ["RightShift"] = Keys.RShiftKey,
            ["LeftControl"] = Keys.LControlKey,
            ["RightControl"] = Keys.RControlKey,
            ["LeftAlt"] = Keys.LMenu,
            ["RightAlt"] = Keys.RMenu,
            ["AltGr"] = Keys.RMenu,
            ["LeftCommand"] = Keys.LWin,
            ["RightCommand"] = Keys.RWin,
            ["LeftWindows"] = Keys.LWin,
            ["RightWindows"] = Keys.RWin,
            ["Backspace"] = Keys.Back,
            ["Return"] = Keys.Enter,
            ["KeypadEnter"] = Keys.Enter,
            ["UpArrow"] = Keys.Up,
            ["DownArrow"] = Keys.Down,
            ["LeftArrow"] = Keys.Left,
            ["RightArrow"] = Keys.Right,
            ["Mouse0"] = Keys.LButton,
            ["Mouse1"] = Keys.RButton,
            ["Mouse2"] = Keys.MButton,
            ["Mouse3"] = Keys.XButton1,
            ["Mouse4"] = Keys.XButton2,
            ["Minus"] = Keys.OemMinus,
            ["Equals"] = Keys.Oemplus,
            ["LeftBracket"] = Keys.OemOpenBrackets,
            ["RightBracket"] = Keys.OemCloseBrackets,
            ["Backslash"] = Keys.OemPipe,
            ["Semicolon"] = Keys.OemSemicolon,
            ["Quote"] = Keys.OemQuotes,
            ["Comma"] = Keys.Oemcomma,
            ["Period"] = Keys.OemPeriod,
            ["Slash"] = Keys.OemQuestion,
            ["BackQuote"] = Keys.Oemtilde,
        };

    private static readonly IReadOnlyDictionary<string, Keys> KeypadAliases =
        new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            ["Period"] = Keys.Decimal,
            ["Divide"] = Keys.Divide,
            ["Multiply"] = Keys.Multiply,
            ["Minus"] = Keys.Subtract,
            ["Plus"] = Keys.Add,
            ["Enter"] = Keys.Enter,
            ["Equals"] = Keys.Oemplus,
        };
}
