using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

/// <summary>
/// Extrait prudemment le temps restant du panneau de raid OCR. Les confusions
/// acceptées sont limitées aux glyphes visuellement proches des chiffres.
/// </summary>
public static partial class RaidTimerParser
{
    public const int MaximumRemainingSeconds = 3 * 60 * 60;

    [GeneratedRegex(
        @"(?<![A-Z0-9])(?:(?<hours>[0-9OILSB]{1,2})\s*[:;]\s*)?(?<minutes>[0-9OILSB]{1,2})\s*[:;]\s*(?<seconds>[0-9OILSB]{2})(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimerPattern();

    public static bool TryParseRemainingSeconds(
        IEnumerable<string>? lines,
        out int remainingSeconds)
    {
        remainingSeconds = 0;
        if (lines is null)
        {
            return false;
        }

        var sourceLines = lines.Select(raw => raw?.Trim() ?? string.Empty).ToArray();
        var candidates = new List<Candidate>();
        var lineIndex = 0;
        foreach (var rawLine in sourceLines)
        {
            var line = rawLine?.Trim() ?? string.Empty;
            if (line.Length == 0)
            {
                lineIndex++;
                continue;
            }

            var labelled = HasTimerLabel(line);
            foreach (Match match in TimerPattern().Matches(line))
            {
                if (!TryParseNumber(match.Groups["minutes"].Value, out var minutes)
                    || !TryParseNumber(match.Groups["seconds"].Value, out var seconds)
                    || seconds >= 60)
                {
                    continue;
                }

                var hours = 0;
                if (match.Groups["hours"].Success)
                {
                    if (!TryParseNumber(match.Groups["hours"].Value, out hours)
                        || minutes >= 60)
                    {
                        continue;
                    }
                }
                // Sans champ des heures, l'affichage doit rester un vrai
                // MM:SS. Cela évite qu'un glyphe d'heure perdu transforme
                // une mauvaise lecture comme 89:42 en durée plausible.
                else if (minutes >= 60)
                {
                    continue;
                }

                var total = checked((hours * 3600) + (minutes * 60) + seconds);
                if (total < 0 || total > MaximumRemainingSeconds)
                {
                    continue;
                }

                candidates.Add(new Candidate(total, labelled, lineIndex));
            }

            lineIndex++;
        }

        // Windows OCR peut réduire le petit H:MM:SS de Tarkov à une chaîne
        // compacte sans mots autour (ex. o e 36 : s ø). N'utiliser ce repli
        // qu'en absence totale d'un chrono standard évite de dégrader une
        // lecture déjà saine ou de transformer une ligne EXFIL en durée.
        if (candidates.Count == 0)
        {
            for (var index = 0; index < sourceLines.Length; index++)
            {
                if (TryParseCompactOcrTimer(sourceLines[index], out var total))
                {
                    candidates.Add(new Candidate(total, HasTimerLabel(sourceLines[index]), index));
                }
            }
        }

        var preferred = candidates.Any(c => c.Labelled)
            ? candidates.Where(c => c.Labelled) : candidates;
        var values = preferred.Select(c => c.RemainingSeconds).Distinct().Take(2).ToArray();
        if (values.Length != 1) return false;
        remainingSeconds = values[0];
        return true;
    }

    private static bool TryParseCompactOcrTimer(string line, out int remainingSeconds)
    {
        remainingSeconds = 0;
        var compact = new string(line.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (compact.Length is not (5 or 7))
        {
            return false;
        }

        var hasHours = compact.Length == 7;
        var firstSeparator = hasHours ? 1 : 2;
        var secondSeparator = hasHours ? 4 : -1;
        if (!IsCompactTimerSeparator(compact[firstSeparator])
            || (hasHours && !IsCompactTimerSeparator(compact[secondSeparator])))
        {
            return false;
        }

        var hours = 0;
        var minuteStart = hasHours ? 2 : 0;
        var secondStart = hasHours ? 5 : 3;
        if ((hasHours && !TryParseNumber(compact[0].ToString(), out hours))
            || !TryParseNumber(compact.Substring(minuteStart, 2), out var minutes)
            || !TryParseNumber(compact.Substring(secondStart, 2), out var seconds)
            || hours > 2
            || minutes >= 60
            || seconds >= 60)
        {
            return false;
        }

        remainingSeconds = (hours * 3600) + (minutes * 60) + seconds;
        return remainingSeconds <= MaximumRemainingSeconds;
    }

    private static bool IsCompactTimerSeparator(char character) =>
        character is ':' or ';' or 'e' or 'E';

    private static bool HasTimerLabel(string line)
    {
        return line.Contains("RAID", StringComparison.OrdinalIgnoreCase)
            || line.Contains("TIME", StringComparison.OrdinalIgnoreCase)
            || line.Contains("TEMPS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNumber(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var character in text)
        {
            var digit = char.ToUpperInvariant(character) switch
            {
                >= '0' and <= '9' => character - '0',
                'O' or 'Ø' => 0,
                'I' or 'L' => 1,
                'S' => 5,
                'B' => 8,
                _ => -1,
            };
            if (digit < 0)
            {
                value = 0;
                return false;
            }
            value = checked((value * 10) + digit);
        }

        return true;
    }

    private readonly record struct Candidate(
        int RemainingSeconds,
        bool Labelled,
        int LineIndex)
    {
        public bool IsBetterThan(Candidate other)
        {
            if (Labelled != other.Labelled)
            {
                return Labelled;
            }
            if (LineIndex != other.LineIndex)
            {
                return LineIndex < other.LineIndex;
            }
            return RemainingSeconds > other.RemainingSeconds;
        }
    }
}
