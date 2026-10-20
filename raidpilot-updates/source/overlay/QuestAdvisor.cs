using System.Globalization;
using System.Text;

namespace RaidPilot.Overlay;

public static class QuestAdvisor
{
    private const double MaximumFuzzyError = 0.18;
    private const double RequiredWinnerMargin = 0.06;

    public static IReadOnlySet<string> ResolveFromOcrLines(
        IEnumerable<QuestCandidate> candidates,
        IEnumerable<string> ocrLines)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ocrLines);

        var entries = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Id))
            .Select(candidate => new CandidateEntry(
                candidate.Id,
                (candidate.Aliases ?? Array.Empty<string>())
                    .Append(candidate.Name)
                    .Select(Normalize)
                    .Where(alias => alias.Length >= 4)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()))
            .Where(entry => entry.Aliases.Count > 0)
            .ToArray();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in ocrLines)
        {
            var line = Normalize(rawLine);
            if (line.Length < 4)
            {
                continue;
            }

            var phraseMatches = new List<PhraseMatch>();
            foreach (var entry in entries)
            {
                var longestAlias = entry.Aliases
                    .Where(alias => line == alias || ContainsWholePhrase(line, alias))
                    .OrderByDescending(alias => alias.Length)
                    .FirstOrDefault();
                if (longestAlias is not null)
                {
                    phraseMatches.Add(new PhraseMatch(entry.Id, longestAlias));
                }
            }

            if (phraseMatches.Count > 0)
            {
                // Garde l'alias maximal : un vrai titre long peut contenir le
                // titre d'une autre quête. Deux alias identiques ou sans lien
                // restent volontairement ambigus et sont tous deux refusés.
                var maximalIds = phraseMatches
                    .Where(match => !phraseMatches.Any(other =>
                        !string.Equals(other.Id, match.Id, StringComparison.OrdinalIgnoreCase)
                        && other.Alias.Length > match.Alias.Length
                        && ContainsWholePhrase(other.Alias, match.Alias)))
                    .Select(match => match.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (maximalIds.Length == 1)
                {
                    result.Add(maximalIds[0]);
                }
                continue;
            }

            var ranked = entries
                .Select(entry => new CandidateScore(
                    entry.Id,
                    entry.Aliases.Min(alias => ScoreLine(line, alias))))
                .Where(item => item.Score <= MaximumFuzzyError)
                .OrderBy(item => item.Score)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            if (ranked.Length == 0)
            {
                continue;
            }

            var best = ranked[0];
            var runnerUp = ranked.Skip(1)
                .FirstOrDefault(item => !string.Equals(
                    item.Id,
                    best.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (runnerUp is not null
                && runnerUp.Score - best.Score < RequiredWinnerMargin)
            {
                continue;
            }

            result.Add(best.Id);
        }

        return result;
    }

    public static string DetectGameMode(
        IEnumerable<string> ocrLines,
        string? currentRaidMode = null)
    {
        ArgumentNullException.ThrowIfNull(ocrLines);
        foreach (var line in ocrLines)
        {
            var tokens = Normalize(line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains("pve", StringComparer.Ordinal))
            {
                return "pve";
            }
        }
        // Le filigrane PvE peut être trop discret sur certaines captures. Une
        // fois détecté dans un raid, son absence sur un F8 suivant ne doit pas
        // rebascule la carte tarkov.dev vers le catalogue normal.
        return currentRaidMode is "pve" or "regular"
            ? currentRaidMode
            : "regular";
    }

    public static bool IsQuestScreen(IEnumerable<string> ocrLines)
    {
        ArgumentNullException.ThrowIfNull(ocrLines);
        var lines = ocrLines
            .Select(Normalize)
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
        {
            return false;
        }

        var hasQuestContext = lines.Any(line => ContainsAnyToken(
            line,
            "task", "tasks", "quest", "quests", "tache", "taches", "quete", "quetes"));
        if (!hasQuestContext)
        {
            return false;
        }

        string[][] headerGroups =
        [
            ["trader", "traders", "marchand", "marchands", "donneur"],
            ["name", "title", "nom", "titre"],
            ["location", "locations", "lieu", "lieux", "emplacement", "emplacements"],
            ["status", "state", "statut", "etat"],
            ["progress", "progression", "progres", "completion", "termine"],
        ];

        var groupsSeen = new bool[headerGroups.Length];
        var maximumGroupsOnOneRow = 0;
        foreach (var line in lines)
        {
            var rowGroupCount = 0;
            for (var index = 0; index < headerGroups.Length; index++)
            {
                if (!ContainsAnyToken(line, headerGroups[index]))
                {
                    continue;
                }

                groupsSeen[index] = true;
                rowGroupCount++;
            }
            maximumGroupsOnOneRow = Math.Max(maximumGroupsOnOneRow, rowGroupCount);
        }

        // Le vrai tableau des quêtes place normalement ses en-têtes sur une
        // rangée. Le repli à trois groupes tolère un OCR qui scinde cette rangée.
        return maximumGroupsOnOneRow >= 2 || groupsSeen.Count(seen => seen) >= 3;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static double ScoreLine(string line, string alias)
    {
        // Les titres courts ressemblent trop facilement à un élément de menu
        // ou à un lieu. Ils ne passent qu'en correspondance exacte.
        if (alias.Length < 8)
        {
            return 1.0;
        }

        var lineTokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aliasTokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (lineTokens.Length == 0 || aliasTokens.Length == 0)
        {
            return 1.0;
        }

        var best = NormalizedEditDistance(line, alias);
        var minimumWindow = Math.Max(1, aliasTokens.Length - 1);
        var maximumWindow = Math.Min(lineTokens.Length, aliasTokens.Length + 1);
        for (var windowSize = minimumWindow; windowSize <= maximumWindow; windowSize++)
        {
            for (var start = 0; start + windowSize <= lineTokens.Length; start++)
            {
                var window = string.Join(' ', lineTokens, start, windowSize);
                var wordPenalty = Math.Abs(windowSize - aliasTokens.Length) * 0.025;
                best = Math.Min(best, NormalizedEditDistance(window, alias) + wordPenalty);
            }
        }
        return best;
    }

    private static bool ContainsWholePhrase(string line, string alias)
    {
        var index = line.IndexOf(alias, StringComparison.Ordinal);
        while (index >= 0)
        {
            var leftBoundary = index == 0 || line[index - 1] == ' ';
            var end = index + alias.Length;
            var rightBoundary = end == line.Length || line[end] == ' ';
            if (leftBoundary && rightBoundary)
            {
                return true;
            }
            index = line.IndexOf(alias, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static bool ContainsAnyToken(string line, params string[] expected)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => expected.Contains(token, StringComparer.Ordinal));
    }

    private static double NormalizedEditDistance(string left, string right)
    {
        var longest = Math.Max(left.Length, right.Length);
        return longest == 0 ? 0.0 : LevenshteinDistance(left, right) / (double)longest;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }
        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record CandidateEntry(string Id, IReadOnlyList<string> Aliases);
    private sealed record CandidateScore(string Id, double Score);
    private sealed record PhraseMatch(string Id, string Alias);
}
