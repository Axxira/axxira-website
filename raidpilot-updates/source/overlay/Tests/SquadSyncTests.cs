using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class SquadSyncTests
{
    internal const int TestCount = 19;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("escouade génère un code partageable", GeneratesShareableCode, failures);
        Run("escouade un seul émetteur Overlay/Beacon local", PreventsDuplicateLocalBroadcast, failures);
        Run("escouade normalise code avec séparateurs", NormalizesFormattedCode, failures);
        Run("escouade refuse caractères ambigus", RejectsAmbiguousCode, failures);
        Run("profil escouade persiste sans perdre l’identité", PersistsProfile, failures);
        Run("rendu escouade Leaflet autonome", HasSquadMapRenderer, failures);
        Run("rendu escouade expire les positions anciennes", HasStaleAndExpiryPolicy, failures);
        Run("rendu escouade refuse les poses hors ordre", RejectsOutOfOrderRenderedPoses, failures);
        Run("rendu escouade projette le cap à travers Leaflet", AppliesMapCoordinateRotation, failures);
        Run("escouade filtre les autres cartes et reconnaît les alias", FiltersForeignMaps, failures);
        Run("pont local rejette les poses périmées et les retours arrière", HasLocalSquadReplayProtection, failures);
        Run("relais Cloudflare utilise notre Worker avec un code sécurisé stable", UsesOwnedRelayEndpoint, failures);
        Run("relais Cloudflare décode les positions retournées", ReadsCloudflarePose, failures);
        Run("questplan accepte les identifiants publics sans journal", ReadsValidatedQuestPlan, failures);
        Run("questplan refuse un faux ID d'objectif", RejectsForgedStage, failures);
        Run("questplan refuse une époque ancienne", RejectsStaleQuestPlan, failures);
        Run("questplan refuse le mode inconnu", RejectsUnknownQuestMode, failures);
        Run("questplan limite les quêtes et étapes diffusées", BoundsQuestPlan, failures);
        Run("questplan accepte uniquement les données locales nécessaires", ValidatesOutgoingQuestPlan, failures);
    }

    private static void PreventsDuplicateLocalBroadcast()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N");
        var team = SquadRoomCode.Generate();
        using var first = new SquadBroadcastLease(team, identity);
        using var second = new SquadBroadcastLease(team, identity);
        Require(first.TryAcquire(), "impossible de démarrer l'émetteur principal");
        Require(!second.TryAcquire(), "Beacon et Overlay peuvent envoyer simultanément");
        first.Release();
        Require(second.TryAcquire(), "le relais secondaire ne reprend pas l'émission");
        Require(!first.TryAcquire(), "l'ancien émetteur a repris malgré le verrou actif");
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

    private static void FiltersForeignMaps()
    {
        Require(OverlayForm.ShouldRenderSquadPoseForMap("customs", "CUSTOMS"),
            "carte identique rejetée");
        Require(OverlayForm.ShouldRenderSquadPoseForMap("labs", "the-lab"),
            "alias de carte valide rejeté");
        Require(!OverlayForm.ShouldRenderSquadPoseForMap("woods", "customs"),
            "pose d’une autre carte acceptée");
    }

    private static void HasLocalSquadReplayProtection()
    {
        var bridge = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LocalMap", "legacy-bridge.js"));
        Require(bridge.Contains("latestMemberTimestamp", StringComparison.Ordinal)
            && bridge.Contains("sequence <= last", StringComparison.Ordinal)
            && bridge.Contains("sequence < last", StringComparison.Ordinal)
            && bridge.Contains("canonicalSquadMap(payload.map)", StringComparison.Ordinal)
            && bridge.Contains("__raidPilotRemoveSquadMember = (id, sentAtUnixMs)", StringComparison.Ordinal),
            "pont escouade local sans protection d’ordre et de changement de carte");
    }

    private static void UsesOwnedRelayEndpoint()
    {
        const string code = "RP-ABCD-EFGH-JKLM-NPQR";
        var endpoint = SquadRelayClient.BuildRelayEndpoint(code, "Émilie Peko");
        Require(endpoint.Scheme == "wss"
            && endpoint.Host == "raidpilot-team-relay.tyronixstudio.workers.dev",
            "le client n'utilise pas le relais Cloudflare RaidPilot");
        Require(endpoint.AbsolutePath == "/ws", "le chemin de connexion est incorrect");
        var key = SquadRelayClient.DeriveRelayTeamKey(code);
        Require(key.Length >= 24 && key.Length <= 64, "la clé dérivée est refusée par Cloudflare");
        Require(key == SquadRelayClient.DeriveRelayTeamKey("ABCDEFGHJKLMNPQR"),
            "le code affiché et le code compact pointent vers deux salles");
        Require(endpoint.Query.Contains("team=" + key, StringComparison.Ordinal),
            "identifiant de salle absent");
        Require(!endpoint.Query.Contains("RP-", StringComparison.Ordinal),
            "code original exposé dans l'URL WebSocket");
    }

    private static void ReadsCloudflarePose()
    {
        using var json = JsonDocument.Parse(
            """{"type":"pose","id":"fake-peer","name":"Amie","map":"customs","position":{"x":102.5,"y":1,"z":-12},"rotation":270,"at":1761170000000}""");
        Require(SquadRelayClient.TryReadPose(json.RootElement, out var pose)
            && pose is not null && pose.X == 102.5 && pose.YawDegrees == 270
            && pose.SentAtUnixMilliseconds == 1761170000000,
            "pose Cloudflare valide rejetée");
        using var malformed = JsonDocument.Parse(
            """{"type":"pose","id":"fake-peer","name":"X","map":"customs","position":{"x":"102","y":1,"z":0},"rotation":270,"at":1761170000000}""");
        Require(!SquadRelayClient.TryReadPose(malformed.RootElement, out _),
            "une coordonnée non numérique a été acceptée");
    }

    private static readonly string QuestA = new('a', 24);
    private static readonly string QuestB = new('b', 24);
    private static string QuestJson(string mode = "pve", string? id = null,
        string? done = null, long? at = null)
    {
        var timestamp = at ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return JsonSerializer.Serialize(new
        {
            type = "questplan", id = "test-peer", name = "Ami",
            mode, at = timestamp, quests = new[] { id ?? QuestA },
            done = new[] { done ?? QuestA + ":" + QuestB }
        });
    }

    private static void ReadsValidatedQuestPlan()
    {
        using var json = JsonDocument.Parse(QuestJson());
        Require(SquadRelayClient.TryReadQuestPlan(json.RootElement, out var plan)
            && plan is not null
            && plan.Mode == "pve"
            && plan.Quests.SequenceEqual([QuestA])
            && plan.Done.SequenceEqual([QuestA + ":" + QuestB]),
            "le plan valide et minimal n'a pas été décodé");
    }

    private static void RejectsForgedStage()
    {
        using var json = JsonDocument.Parse(QuestJson(done: "player@mail.com:secret"));
        Require(!SquadRelayClient.TryReadQuestPlan(json.RootElement, out _),
            "des données autres que les IDs de quêtes ont été acceptées");
    }

    private static void RejectsStaleQuestPlan()
    {
        using var json = JsonDocument.Parse(
            QuestJson(at: DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeMilliseconds()));
        Require(!SquadRelayClient.TryReadQuestPlan(json.RootElement, out _),
            "une ancienne mise à jour pourrait réactiver une progression périmée");
    }

    private static void RejectsUnknownQuestMode()
    {
        using var json = JsonDocument.Parse(QuestJson(mode: "unknown"));
        Require(!SquadRelayClient.TryReadQuestPlan(json.RootElement, out _),
            "le mode d'un autre profil est accepté");
    }

    private static void BoundsQuestPlan()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = JsonSerializer.Serialize(new {
            type = "questplan", id = "test-peer", name = "Ami",
            mode = "pve", at = now, quests = Enumerable.Repeat(QuestA, 81).ToArray(),
            done = Array.Empty<string>()
        });
        using var json = JsonDocument.Parse(payload);
        Require(!SquadRelayClient.TryReadQuestPlan(json.RootElement, out _),
            "un plan excessif n'a pas été limité");
    }

    private static void ValidatesOutgoingQuestPlan()
    {
        Require(SquadRelayClient.ValidQuestPlan(
            new SquadQuestPlanSnapshot("pve", [QuestA], [QuestA + ":" + QuestB])),
            "l'export minimal est refusé");
        Require(!SquadRelayClient.ValidQuestPlan(
            new SquadQuestPlanSnapshot("unknown", [QuestA], [])),
            "un mode inconnu est exportable");
        Require(!SquadRelayClient.ValidQuestPlan(
            new SquadQuestPlanSnapshot("pve", ["Player Profile"], [])),
            "un nom de joueur fuit vers le relais");
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
