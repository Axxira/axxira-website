using System.Globalization;
using System.Text;

namespace RaidPilot.Overlay;

public enum RaidSide
{
    Pmc,
    Scav,
}

[Flags]
public enum ExtractionRequirement
{
    None = 0,
    Switch = 1 << 0,
    Transfer = 1 << 1,
    Cooperation = 1 << 2,
    Unknown = 1 << 3,
}

/// <summary>
/// Une sortie tarkov.dev exprimée dans le même repère monde X/Y/Z que les
/// coordonnées encodées dans le nom des captures EFT.
/// </summary>
public sealed record ExtractionCandidate(
    string Id,
    string Name,
    string? Faction,
    double X,
    double Y,
    double Z,
    ExtractionRequirement Requirements = ExtractionRequirement.None,
    IReadOnlyList<string>? Aliases = null);

public enum ExtractionSelectionReason
{
    ConfirmedNearest,
    UserPreferred,
    UnconfirmedFallback,
}

/// <summary>
/// Sélection de cible. La distance est à vol d'oiseau dans le plan X/Z : elle
/// ne prétend pas représenter un chemin praticable autour des murs ou étages.
/// </summary>
public sealed record ExtractionRecommendation(
    ExtractionCandidate Target,
    double DirectDistanceMeters,
    double VerticalDeltaMeters,
    ExtractionSelectionReason Reason,
    bool NeedsAvailabilityConfirmation);

public sealed record OcrExtractionResolution(
    int SlotCount,
    IReadOnlySet<string> ConfirmedIds)
{
    public bool IsComplete => SlotCount >= 2 && ConfirmedIds.Count == SlotCount;
}

/// <summary>
/// Choisit une extraction parmi les sorties compatibles avec le rôle et,
/// idéalement, confirmées dans le panneau d'extractions du raid.
/// </summary>
public static class ExtractionAdvisor
{
    /// <summary>
    /// Choisit une cible.
    /// </summary>
    /// <param name="confirmedExtractionIds">
    /// IDs lus/choisis depuis le panneau d'extractions du raid. Une collection
    /// vide signifie « aucune sortie confirmée »; <see langword="null"/>
    /// signifie que le panneau n'a pas encore été renseigné.
    /// </param>
    /// <param name="preferredId">
    /// Cible verrouillée par l'utilisateur. Elle est conservée tant qu'elle
    /// reste admissible, ce qui évite que la flèche change de sortie à chaque
    /// recalibrage.
    /// </param>
    /// <param name="allowConditional">
    /// Autorise les sorties avec paiement, coopération, interrupteur ou
    /// condition inconnue. Elles sont exclues par défaut.
    /// </param>
    /// <param name="allowUnconfirmedFallback">
    /// Autorise une suggestion parmi les sorties seulement possibles. Le
    /// résultat porte alors <c>NeedsAvailabilityConfirmation = true</c> et ne
    /// doit jamais être présenté comme une sortie garantie.
    /// </param>
    public static bool TryChoose(
        ScreenshotObservation player,
        RaidSide side,
        IEnumerable<ExtractionCandidate> candidates,
        IReadOnlySet<string>? confirmedExtractionIds,
        out ExtractionRecommendation? recommendation,
        string? preferredId = null,
        bool allowConditional = false,
        bool allowUnconfirmedFallback = false)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(candidates);

        recommendation = null;
        if (!CoordinatesAreFinite(player.X, player.Y, player.Z))
        {
            return false;
        }

        var confirmed = confirmedExtractionIds is null
            ? null
            : new HashSet<string>(confirmedExtractionIds, StringComparer.OrdinalIgnoreCase);

        var usable = confirmed is null
            ? Array.Empty<CandidateDistance>()
            : candidates
                .Where(IsValid)
                .Where(candidate => IsCompatibleWithSide(candidate, side, confirmed))
                .Where(candidate => confirmed.Contains(candidate.Id))
                .Where(candidate => allowConditional || EffectiveRequirements(candidate) == ExtractionRequirement.None)
                .Select(candidate => new CandidateDistance(
                    candidate,
                    HorizontalDistance(player, candidate),
                    candidate.Y - player.Y))
                .OrderBy(item => item.DistanceMeters)
                .ThenBy(item => item.Candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        var usingUnconfirmedFallback = false;
        if (usable.Length == 0 && confirmed is null && allowUnconfirmedFallback)
        {
            usable = candidates
                .Where(IsValid)
                .Where(candidate => IsCompatibleWithSide(candidate, side, confirmedIds: null))
                .Where(candidate => allowConditional || EffectiveRequirements(candidate) == ExtractionRequirement.None)
                .Select(candidate => new CandidateDistance(
                    candidate,
                    HorizontalDistance(player, candidate),
                    candidate.Y - player.Y))
                .OrderBy(item => item.DistanceMeters)
                .ThenBy(item => item.Candidate.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            usingUnconfirmedFallback = usable.Length > 0;
        }

        if (usable.Length == 0)
        {
            return false;
        }

        var preferred = string.IsNullOrWhiteSpace(preferredId)
            ? null
            : usable.FirstOrDefault(item =>
                string.Equals(item.Candidate.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        var chosen = preferred ?? usable[0];

        var reason = preferred is not null
            ? ExtractionSelectionReason.UserPreferred
            : usingUnconfirmedFallback
                ? ExtractionSelectionReason.UnconfirmedFallback
                : ExtractionSelectionReason.ConfirmedNearest;

        recommendation = new ExtractionRecommendation(
            chosen.Candidate,
            chosen.DistanceMeters,
            chosen.VerticalDeltaMeters,
            reason,
            NeedsAvailabilityConfirmation: usingUnconfirmedFallback);
        return true;
    }

    /// <summary>
    /// Choisit d'abord une sortie confirmée sans condition. Certaines cartes,
    /// notamment Reserve côté PMC, n'en proposent aucune : dans ce cas
    /// seulement, une sortie conditionnelle explicitement lue dans le panneau
    /// EXFIL du raid devient admissible. Les sorties non confirmées ne sont
    /// jamais utilisées par ce secours.
    /// </summary>
    public static bool TryChooseConfirmedWithConditionalFallback(
        ScreenshotObservation player,
        RaidSide side,
        IEnumerable<ExtractionCandidate> candidates,
        IReadOnlySet<string> confirmedExtractionIds,
        out ExtractionRecommendation? recommendation)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(confirmedExtractionIds);

        var candidateArray = candidates as ExtractionCandidate[] ?? candidates.ToArray();
        if (TryChoose(
                player,
                side,
                candidateArray,
                confirmedExtractionIds,
                out recommendation,
                allowConditional: false,
                allowUnconfirmedFallback: false))
        {
            return true;
        }

        return TryChoose(
            player,
            side,
            candidateArray,
            confirmedExtractionIds,
            out recommendation,
            allowConditional: true,
            allowUnconfirmedFallback: false);
    }

    /// <summary>
    /// Résout uniquement les correspondances de nom exactes après suppression
    /// des accents et de la ponctuation. Un nom ambigu est volontairement
    /// ignoré pour qu'un OCR imparfait ne sélectionne pas la mauvaise sortie.
    /// </summary>
    public static IReadOnlySet<string> ResolveConfirmedIdsByDisplayedName(
        IEnumerable<ExtractionCandidate> candidates,
        IEnumerable<string> displayedNames)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(displayedNames);

        var byName = BuildUniqueAliasIndex(candidates)
            .ToDictionary(entry => entry.Alias, entry => entry.Id, StringComparer.Ordinal);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var displayedName in displayedNames)
        {
            var normalized = NormalizeDisplayedName(displayedName);
            if (byName.TryGetValue(normalized, out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    /// <summary>
    /// Résout les lignes OCR du panneau O. Tarkov préfixe souvent chaque ligne
    /// par EXFILxx et peut ajouter un état à droite; un alias est donc recherché
    /// comme une suite de mots complète à l'intérieur de la ligne. Une petite
    /// correction OCR est tolérée uniquement pour les noms assez longs et
    /// seulement si elle désigne une sortie sans ambiguïté.
    /// </summary>
    public static IReadOnlySet<string> ResolveConfirmedIdsFromOcrLines(
        IEnumerable<ExtractionCandidate> candidates,
        IEnumerable<string> ocrLines)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ocrLines);

        var aliases = BuildUniqueAliasIndex(candidates)
            .OrderByDescending(entry => entry.Alias.Length)
            .ToArray();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in ocrLines)
        {
            var line = NormalizeOcrLine(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            var exactMatches = aliases
                .Where(entry => ContainsWholePhrase(line, entry.Alias))
                .ToArray();
            if (exactMatches.Length > 0)
            {
                foreach (var match in RemoveShadowedShortAliases(exactMatches))
                {
                    result.Add(match.Id);
                }
                continue;
            }

            var fuzzyMatches = aliases
                .Select(entry => new
                {
                    Entry = entry,
                    Distance = BestTokenWindowDistance(line, entry.Alias),
                })
                .Where(item => item.Distance >= 0)
                .OrderBy(item => item.Distance)
                .ThenByDescending(item => item.Entry.Alias.Length)
                .ToArray();
            if (fuzzyMatches.Length == 0)
            {
                continue;
            }

            var best = fuzzyMatches[0];
            var tiedIds = fuzzyMatches
                .Where(item => item.Distance == best.Distance)
                .Select(item => item.Entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (tiedIds.Length == 1)
            {
                result.Add(best.Entry.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Déduplique les rangs EXFIL lus par OCR, puis exige exactement une sortie
    /// unique par rang. Une ligne du HUD ou une lecture ambiguë ne peut donc pas
    /// compléter artificiellement le panneau.
    /// </summary>
    public static OcrExtractionResolution ResolveOcrExtractionPanel(
        IEnumerable<ExtractionCandidate> candidates,
        IEnumerable<string> ocrLines)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(ocrLines);

        var candidateArray = candidates.ToArray();
        var exfilLines = SelectUniqueOcrExfilLines(ocrLines);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in exfilLines)
        {
            var lineMatches = ResolveConfirmedIdsFromOcrLines(candidateArray, [line]);
            if (lineMatches.Count == 1)
            {
                ids.Add(lineMatches.Single());
            }
        }

        return new OcrExtractionResolution(exfilLines.Count, ids);
    }

    /// <summary>
    /// Garde une seule ligne par numéro EXFIL et tolère les confusions OCR
    /// usuelles I/1 ainsi que O/0/Ø dans le préfixe numérique.
    /// </summary>
    public static IReadOnlyList<string> SelectUniqueOcrExfilLines(
        IEnumerable<string> ocrLines)
    {
        ArgumentNullException.ThrowIfNull(ocrLines);

        var bySlot = new Dictionary<int, string>();
        int? wrappedSlot = null;
        foreach (var rawLine in ocrLines)
        {
            if (!TryReadOcrExfilSlot(rawLine, out var slot))
            {
                if (wrappedSlot is { } continuationSlot
                    && bySlot.TryGetValue(continuationSlot, out var wrappedLine)
                    && !string.IsNullOrWhiteSpace(rawLine))
                {
                    var combined = $"{wrappedLine} {rawLine.Trim()}";
                    bySlot[continuationSlot] = combined;
                    if (!HasUnclosedParenthesis(combined))
                    {
                        wrappedSlot = null;
                    }
                }
                continue;
            }

            var line = rawLine.Trim();
            if (!bySlot.TryGetValue(slot, out var current)
                || NormalizeDisplayedName(line).Length > NormalizeDisplayedName(current).Length)
            {
                bySlot[slot] = line;
            }
            wrappedSlot = HasUnclosedParenthesis(line) ? slot : null;
        }

        return bySlot
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .ToArray();
    }

    private static bool HasUnclosedParenthesis(string value)
    {
        return value.Count(character => character == '(')
            > value.Count(character => character == ')');
    }

    private static bool TryReadOcrExfilSlot(string? rawLine, out int slot)
    {
        slot = 0;
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var normalized = NormalizeDisplayedName(rawLine);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens[0].Length < 5)
        {
            return false;
        }

        var first = tokens[0];
        var prefix = new string(first[..5].Select(character => character switch
        {
            '1' => 'i',
            _ => character,
        }).ToArray());
        if (!string.Equals(prefix, "exfil", StringComparison.Ordinal))
        {
            return false;
        }

        var rankToken = first.Length > 5
            ? first[5..]
            : tokens.Length > 1 ? tokens[1] : string.Empty;
        if (rankToken.Length == 0)
        {
            return false;
        }

        // Après normalisation, un rang absent comme « EXFIL?? Sortie… »
        // devient « exfil sortie… ». Ne jamais extraire les lettres o/i de ce
        // premier mot comme si elles formaient le rang 01 : le jeton entier
        // doit être composé uniquement de chiffres et de leurs confusions OCR.
        if (rankToken.Any(character => character is not (
                >= '0' and <= '9'
                or 'o'
                or 'ø'
                or 'i'
                or 'l')))
        {
            return false;
        }

        var digits = new string(rankToken.Select(character => character switch
        {
            'o' or 'ø' => '0',
            'i' or 'l' => '1',
            >= '0' and <= '9' => character,
            _ => character,
        }).ToArray());
        return digits.Length is > 0 and <= 3
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot is >= 1 and <= 99;
    }

    private static string NormalizeOcrLine(string? value)
    {
        var normalized = NormalizeDisplayedName(value);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (!token.Any(char.IsDigit) && !token.Contains('ø'))
            {
                continue;
            }

            tokens[index] = new string(token.Select(character => character switch
            {
                'o' or 'ø' => '0',
                'i' or 'l' => '1',
                _ => character,
            }).ToArray());
        }

        return string.Join(' ', tokens);
    }

    public static string NormalizeDisplayedName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    public static ExtractionRequirement EffectiveRequirements(ExtractionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.Requirements;
    }

    private static IReadOnlyList<AliasEntry> BuildUniqueAliasIndex(
        IEnumerable<ExtractionCandidate> candidates)
    {
        return candidates
            .Where(IsValid)
            .SelectMany(candidate => (candidate.Aliases ?? Array.Empty<string>())
                .Append(candidate.Name)
                .Select(alias => new AliasEntry(
                    candidate.Id,
                    NormalizeDisplayedName(alias))))
            .Where(entry => entry.Alias.Length > 0)
            .Distinct()
            .GroupBy(entry => entry.Alias, StringComparer.Ordinal)
            .Where(group => group
                .Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool ContainsWholePhrase(string line, string phrase)
    {
        return string.Equals(line, phrase, StringComparison.Ordinal)
            || $" {line} ".Contains($" {phrase} ", StringComparison.Ordinal);
    }

    private static IEnumerable<AliasEntry> RemoveShadowedShortAliases(
        IReadOnlyList<AliasEntry> matches)
    {
        foreach (var match in matches)
        {
            var shadowed = matches.Any(other =>
                !string.Equals(other.Id, match.Id, StringComparison.OrdinalIgnoreCase)
                && other.Alias.Length > match.Alias.Length
                && ContainsWholePhrase(other.Alias, match.Alias));
            if (!shadowed)
            {
                yield return match;
            }
        }
    }

    private static int BestTokenWindowDistance(string line, string alias)
    {
        if (alias.Length < 6)
        {
            return -1;
        }

        var aliasTokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lineTokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (aliasTokens.Length == 0 || lineTokens.Length < aliasTokens.Length)
        {
            return -1;
        }

        var allowedDistance = alias.Length >= 12 ? 2 : 1;
        var best = int.MaxValue;
        for (var start = 0; start <= lineTokens.Length - aliasTokens.Length; start++)
        {
            var window = string.Join(' ', lineTokens, start, aliasTokens.Length);
            if (Math.Abs(window.Length - alias.Length) > allowedDistance)
            {
                continue;
            }

            var distance = LevenshteinDistanceWithin(window, alias, allowedDistance);
            best = Math.Min(best, distance);
        }

        return best <= allowedDistance ? best : -1;
    }

    private static int LevenshteinDistanceWithin(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit)
        {
            return limit + 1;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            var rowMinimum = current[0];
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1]
                    + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1);
                current[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[rightIndex]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record AliasEntry(string Id, string Alias);

    private static bool IsCompatibleWithSide(
        ExtractionCandidate candidate,
        RaidSide side,
        IReadOnlySet<string>? confirmedIds)
    {
        // A sortie explicitement confirmée dans le raid l'emporte sur un champ
        // faction absent ou temporairement incohérent dans les données publiques.
        if (confirmedIds?.Contains(candidate.Id) == true)
        {
            return true;
        }

        var expected = side == RaidSide.Pmc ? "pmc" : "scav";
        return string.IsNullOrWhiteSpace(candidate.Faction)
            || string.Equals(candidate.Faction, expected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Faction, "shared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValid(ExtractionCandidate candidate)
    {
        return candidate is not null
            && !string.IsNullOrWhiteSpace(candidate.Id)
            && !string.IsNullOrWhiteSpace(candidate.Name)
            && CoordinatesAreFinite(candidate.X, candidate.Y, candidate.Z);
    }

    private static bool CoordinatesAreFinite(double x, double y, double z)
    {
        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
    }

    private static double HorizontalDistance(
        ScreenshotObservation player,
        ExtractionCandidate candidate)
    {
        var deltaX = candidate.X - player.X;
        var deltaZ = candidate.Z - player.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }

    private sealed record CandidateDistance(
        ExtractionCandidate Candidate,
        double DistanceMeters,
        double VerticalDeltaMeters);
}
