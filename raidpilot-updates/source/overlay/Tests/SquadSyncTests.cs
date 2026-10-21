namespace RaidPilot.Overlay.Tests;

internal static class SquadSyncTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("escouade génère un code partageable", GeneratesShareableCode, failures);
        Run("escouade normalise code avec séparateurs", NormalizesFormattedCode, failures);
        Run("escouade refuse caractères ambigus", RejectsAmbiguousCode, failures);
        Run("profil escouade persiste sans perdre l’identité", PersistsProfile, failures);
        Run("rendu escouade Leaflet autonome", HasSquadMapRenderer, failures);
        Run("rendu escouade expire les positions anciennes", HasStaleAndExpiryPolicy, failures);
        Run("rendu escouade refuse les poses hors ordre", RejectsOutOfOrderRenderedPoses, failures);
        Run("rendu escouade projette le cap à travers Leaflet", AppliesMapCoordinateRotation, failures);
    }

    private static void GeneratesShareableCode()
    {
        var code = SquadRoomCode.Generate();
        Require(code.StartsWith("RP-", StringComparison.Ordinal), "préfixe RP absent");
        Require(code.Length == 22, $"longueur inattendue : {code.Length}");
        Require(SquadRoomCode.TryNormalize(code, out var normalized), "code généré non relisible");
        Require(normalized.Length == 16, "code compact doit contenir 16 caractères");
        Require(SquadRoomCode.ToSessionId(code).StartsWith("RPSQ", StringComparison.Ordinal),
            "session relais non namespacée");
    }

    private static void NormalizesFormattedCode()
    {
        const string code = "RP-ABCD-EFGH-JKLM-NPQR";
        Require(SquadRoomCode.TryNormalize(code.ToLowerInvariant(), out var normalized),
            "code minuscule non normalisé");
        Require(normalized == "ABCDEFGHJKLMNPQR", $"normalisation reçue : {normalized}");
        Require(SquadRoomCode.Format(normalized) == code, "formatage non stable");
    }

    private static void RejectsAmbiguousCode()
    {
        Require(!SquadRoomCode.TryNormalize("RP-ABCI-EFGH-JKLM-NPQR", out _),
            "I ambigu accepté");
        Require(!SquadRoomCode.TryNormalize("RP-ABCO-EFGH-JKLM-NPQR", out _),
            "O ambigu accepté");
        Require(!SquadRoomCode.TryNormalize("RP-ABC0-EFGH-JKLM-NPQR", out _),
            "0 ambigu accepté");
        Require(!SquadRoomCode.TryNormalize("RP-ABC1-EFGH-JKLM-NPQR", out _),
            "1 ambigu accepté");
    }

    private static void PersistsProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "raidpilot-squad-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "profile.json");
        try
        {
            var profile = new SquadProfile(
                "member-" + Guid.NewGuid().ToString("N"),
                "Pierre",
                "RP-ABCD-EFGH-JKLM-NPQR",
                true);
            Require(SquadProfileStore.TrySave(profile, path), "sauvegarde refusée");
            var read = SquadProfileStore.LoadOrCreate(path);
            Require(read.MemberId == profile.MemberId, "identité modifiée");
            Require(read.Name == "Pierre", "nom modifié");
            Require(read.RoomCode == "RP-ABCD-EFGH-JKLM-NPQR", "code perdu");
            Require(read.AutoJoin, "auto-join perdu");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void HasSquadMapRenderer()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css"));
        Require(behavior.Contains("__raidPilotSetSquadPose", StringComparison.Ordinal)
            && behavior.Contains("latLngToContainerPoint([entry.pose.z, entry.pose.x])", StringComparison.Ordinal)
            && behavior.Contains("raidpilot-squad-marker", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-marker", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-label", StringComparison.Ordinal),
            "pions escouade non projetés indépendamment");
    }

    private static void HasStaleAndExpiryPolicy()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        Require(behavior.Contains("squadMemberStaleMs = 2500", StringComparison.Ordinal)
            && behavior.Contains("squadMemberExpiryMs = 8000", StringComparison.Ordinal)
            && behavior.Contains("__raidPilotRemoveSquadMember", StringComparison.Ordinal)
            && behavior.Contains("__raidPilotClearSquad", StringComparison.Ordinal),
            "politique de péremption escouade absente");
    }

    private static void RejectsOutOfOrderRenderedPoses()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css"));
        Require(behavior.Contains("Number.isSafeInteger(sentAtUnixMs)", StringComparison.Ordinal)
            && behavior.Contains("pose.sentAtUnixMs <= entry.pose.sentAtUnixMs", StringComparison.Ordinal)
            && styles.Contains("transition: transform 100ms linear", StringComparison.Ordinal),
            "ordre temporel ou interpolation 10 Hz des teammates absent");
    }

    private static void AppliesMapCoordinateRotation()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        Require(
            behavior.Contains("raidPilotProjectedDisplayYaw", StringComparison.Ordinal)
            && behavior.Contains("entry.pose.x,", StringComparison.Ordinal)
            && behavior.Contains("entry.pose.z,", StringComparison.Ordinal)
            && behavior.Contains("Math.atan2(dx, -dy)", StringComparison.Ordinal),
            "les flèches teammates ne projettent pas leur direction par Leaflet");
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
            failures.Add($"{name}: {exception.Message}");
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
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