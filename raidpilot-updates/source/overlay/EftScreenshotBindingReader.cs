using System.Text.Json;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

public enum EftScreenshotBindingStatus
{
    Unavailable,
    Unbound,
    NeedsRebind,
    Ready,
}

public sealed record EftScreenshotBindingInfo(
    EftScreenshotBindingStatus Status,
    string UserLabel,
    IReadOnlyList<string> KeyCodes,
    string? AxisName,
    bool? PositiveAxis)
{
    public bool IsReady => Status == EftScreenshotBindingStatus.Ready;
}

/// <summary>
/// Reads EFT's screenshot binding without changing Control.ini. The reader is
/// deliberately fail-closed: unavailable or malformed settings never become a
/// guessed key.
/// </summary>
public static partial class EftScreenshotBindingReader
{
    private const long MaximumControlFileBytes = 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> FriendlyKeyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LeftControl"] = "Ctrl",
            ["RightControl"] = "Ctrl",
            ["LeftAlt"] = "Alt",
            ["RightAlt"] = "Alt",
            ["AltGr"] = "AltGr",
            ["LeftShift"] = "Maj",
            ["RightShift"] = "Maj",
            ["LeftCommand"] = "Windows",
            ["RightCommand"] = "Windows",
            ["LeftWindows"] = "Windows",
            ["RightWindows"] = "Windows",
            ["SysReq"] = "Impr. écran",
            ["Escape"] = "Échap",
            ["Return"] = "Entrée",
            ["KeypadEnter"] = "Entrée pavé numérique",
            ["Space"] = "Espace",
            ["Backspace"] = "Retour arrière",
            ["Tab"] = "Tab",
            ["Delete"] = "Suppr",
            ["Insert"] = "Inser",
            ["PageUp"] = "Page précédente",
            ["PageDown"] = "Page suivante",
            ["Home"] = "Début",
            ["End"] = "Fin",
            ["UpArrow"] = "Flèche haut",
            ["DownArrow"] = "Flèche bas",
            ["LeftArrow"] = "Flèche gauche",
            ["RightArrow"] = "Flèche droite",
            ["Mouse0"] = "Clic gauche",
            ["Mouse1"] = "Clic droit",
            ["Mouse2"] = "Clic molette",
        };

    public static string DefaultControlIniPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Battlestate Games",
        "Escape from Tarkov",
        "Settings",
        "Control.ini");

    public static EftScreenshotBindingInfo Read(string? controlIniPath = null)
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
                return Unavailable();
            }

            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 32,
            });
            return ReadDocument(document.RootElement);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return Unavailable();
        }
    }

    public static bool TryParse(string? json, out EftScreenshotBindingInfo binding)
    {
        binding = Unavailable();
        if (string.IsNullOrWhiteSpace(json)
            || json.Length > MaximumControlFileBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 32,
            });
            binding = ReadDocument(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static EftScreenshotBindingInfo ReadDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("keyBindings", out var keyBindings)
            || keyBindings.ValueKind != JsonValueKind.Array)
        {
            return Unbound();
        }

        foreach (var keyBinding in keyBindings.EnumerateArray())
        {
            if (keyBinding.ValueKind != JsonValueKind.Object
                || !keyBinding.TryGetProperty("keyName", out var keyName)
                || keyName.ValueKind != JsonValueKind.String
                || !string.Equals(keyName.GetString(), "MakeScreenshot", StringComparison.Ordinal)
                || !keyBinding.TryGetProperty("variants", out var variants)
                || variants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            EftScreenshotBindingInfo? fallback = null;
            foreach (var variant in variants.EnumerateArray())
            {
                var parsed = ReadVariant(variant);
                if (parsed is null)
                {
                    continue;
                }

                if (parsed.Status == EftScreenshotBindingStatus.Ready)
                {
                    return parsed;
                }

                fallback ??= parsed;
            }

            return fallback ?? Unbound();
        }

        return Unbound();
    }

    private static EftScreenshotBindingInfo? ReadVariant(JsonElement variant)
    {
        if (variant.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var keyCodes = new List<string>();
        if (variant.TryGetProperty("keyCode", out var keyCode)
            && keyCode.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in keyCode.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    keyCodes.Add(value.GetString()!.Trim());
                }
            }
        }

        var isAxis = variant.TryGetProperty("isAxis", out var isAxisProperty)
            && isAxisProperty.ValueKind is JsonValueKind.True;
        string? axisName = null;
        bool? positiveAxis = null;
        if (isAxis)
        {
            if (variant.TryGetProperty("axisName", out var axisNameProperty)
                && axisNameProperty.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(axisNameProperty.GetString()))
            {
                axisName = axisNameProperty.GetString()!.Trim();
            }

            positiveAxis = variant.TryGetProperty("positiveAxis", out var positiveAxisProperty)
                && positiveAxisProperty.ValueKind is JsonValueKind.True;
        }

        if (keyCodes.Count == 0 && axisName is null)
        {
            return null;
        }

        var needsRebind = keyCodes.Any(key => string.Equals(key, "SysReq", StringComparison.OrdinalIgnoreCase));
        var label = FormatLabel(keyCodes, axisName, positiveAxis);
        if (needsRebind)
        {
            label += " · à remplacer";
        }

        return new EftScreenshotBindingInfo(
            needsRebind ? EftScreenshotBindingStatus.NeedsRebind : EftScreenshotBindingStatus.Ready,
            label,
            keyCodes.AsReadOnly(),
            axisName,
            positiveAxis);
    }

    private static string FormatLabel(
        IReadOnlyCollection<string> keyCodes,
        string? axisName,
        bool? positiveAxis)
    {
        var modifiers = keyCodes
            .Where(IsModifier)
            .Select(FormatKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var regularKeys = keyCodes
            .Where(key => !IsModifier(key))
            .Select(FormatKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var parts = modifiers.Concat(regularKeys).ToList();

        if (axisName is not null)
        {
            parts.Add(FormatAxis(axisName, positiveAxis == true));
        }

        return string.Join(" + ", parts);
    }

    private static bool IsModifier(string key) => key.EndsWith("Control", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("Alt", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("Shift", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("Windows", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "AltGr", StringComparison.OrdinalIgnoreCase);

    private static string FormatKey(string key)
    {
        if (FriendlyKeyNames.TryGetValue(key, out var friendlyName))
        {
            return friendlyName;
        }

        var alphaMatch = AlphaKeyPattern().Match(key);
        if (alphaMatch.Success)
        {
            return alphaMatch.Groups[1].Value;
        }

        var keypadMatch = KeypadPattern().Match(key);
        if (keypadMatch.Success)
        {
            return $"Pavé {keypadMatch.Groups[1].Value}";
        }

        var mouseMatch = MouseButtonPattern().Match(key);
        if (mouseMatch.Success
            && int.TryParse(mouseMatch.Groups[1].Value, out var zeroBasedButton))
        {
            return $"Bouton souris {zeroBasedButton + 1}";
        }

        return key;
    }

    private static string FormatAxis(string axisName, bool positiveAxis)
    {
        if (string.Equals(axisName, "Mouse Scroll", StringComparison.OrdinalIgnoreCase))
        {
            return positiveAxis ? "Molette vers le haut" : "Molette vers le bas";
        }

        return positiveAxis ? $"{axisName} +" : $"{axisName} -";
    }

    private static EftScreenshotBindingInfo Unavailable() => new(
        EftScreenshotBindingStatus.Unavailable,
        "Touche de position indisponible",
        Array.Empty<string>(),
        null,
        null);

    private static EftScreenshotBindingInfo Unbound() => new(
        EftScreenshotBindingStatus.Unbound,
        "Touche de position non configurée",
        Array.Empty<string>(),
        null,
        null);

    [GeneratedRegex("^Alpha([0-9])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AlphaKeyPattern();

    [GeneratedRegex("^Keypad(.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex KeypadPattern();

    [GeneratedRegex("^Mouse([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MouseButtonPattern();
}
