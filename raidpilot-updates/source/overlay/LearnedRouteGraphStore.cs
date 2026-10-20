using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaidPilot.Overlay;

/// <summary>
/// Persistance JSON bornée et validée du graphe appris. Une session active
/// n'est jamais restaurée : un nouveau raid doit toujours appeler BeginSession.
/// </summary>
public static class LearnedRouteGraphStore
{
    internal const int SchemaVersion = 1;
    // Les limites du graphe permettent jusqu'à 16 cartes, 65 536 nœuds et
    // 262 144 arêtes. Leur JSON minimal dépasse déjà 38 Mio; 128 Mio garde une
    // marge pour les coordonnées et horodatages réalistes sans rendre la
    // lecture non bornée.
    internal const long MaximumFileBytes = 128L * 1024L * 1024L;

    // Les sauvegardes d'un même fichier sont sérialisées. Le dictionnaire est
    // volontairement conservé pour la durée de vie du processus : RaidPilot
    // n'utilise qu'un chemin de graphe et retirer un verrou encore convoité
    // pourrait permettre deux écritures concurrentes.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveGates =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        MaxDepth = 32,
    };

    public static bool TrySave(string path, LearnedRouteGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!TryResolvePath(path, out var fullPath))
        {
            return false;
        }

        LearnedRouteSaveSnapshot snapshot;
        try
        {
            snapshot = CaptureSnapshot(graph);
        }
        catch
        {
            return false;
        }

        var saveGate = SaveGates.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        saveGate.Wait();
        try
        {
            return TryWriteSnapshot(fullPath, snapshot, CancellationToken.None);
        }
        finally
        {
            saveGate.Release();
        }
    }

    /// <summary>
    /// Capture un instantané cohérent du graphe sur le fil appelant, puis
    /// sérialise et écrit cet instantané hors de ce fil. La session active du
    /// raid ne fait volontairement pas partie du modèle de persistance.
    /// </summary>
    public static Task<bool> TrySaveAsync(
        string path,
        LearnedRouteGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolvePath(path, out var fullPath))
        {
            return Task.FromResult(false);
        }

        LearnedRouteSaveSnapshot snapshot;
        try
        {
            // CreatePersistenceModel effectue toute l'énumération sous le
            // verrou du graphe. La conversion ci-dessous donne ensuite au
            // travail de fond des collections privées qu'aucun autre fil ne
            // peut modifier.
            snapshot = CaptureSnapshot(graph);
        }
        catch
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return SaveSnapshotAsync(fullPath, snapshot, cancellationToken);
    }

    public static bool TryLoad(
        string path,
        out LearnedRouteGraph? graph,
        LearnedRouteGraphOptions? options = null)
    {
        graph = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length is <= 0 or > MaximumFileBytes)
            {
                return false;
            }

            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            var model = JsonSerializer.Deserialize<LearnedRoutePersistenceModel>(stream, JsonOptions);
            return LearnedRouteGraph.TryCreateFromPersistenceModel(model, options, out graph);
        }
        catch
        {
            graph = null;
            return false;
        }
    }

    private static async Task<bool> SaveSnapshotAsync(
        string fullPath,
        LearnedRouteSaveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var saveGate = SaveGates.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Task.Run est intentionnel : JsonSerializer.SerializeToUtf8Bytes
            // et Flush(true) sont synchrones et ne doivent jamais geler le
            // thread d'interface.
            return await Task.Run(
                    () => TryWriteSnapshot(fullPath, snapshot, cancellationToken),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            saveGate.Release();
        }
    }

    private static bool TryWriteSnapshot(
        string fullPath,
        LearnedRouteSaveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            if (bytes.LongLength > MaximumFileBytes)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            // Une annulation n'est honorée qu'avant le renommage. Ainsi, la
            // tâche ne peut jamais être déclarée annulée après avoir publié le
            // nouveau fichier.
            cancellationToken.ThrowIfCancellationRequested();
            CommitTemporaryFile(temporaryPath, fullPath);
            temporaryPath = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }

    private static LearnedRouteSaveSnapshot CaptureSnapshot(LearnedRouteGraph graph)
    {
        var model = graph.CreatePersistenceModel();
        var maps = model.Maps ?? new List<LearnedRouteMapPersistenceModel>();
        return new LearnedRouteSaveSnapshot(
            model.SchemaVersion,
            maps.Select(static map => new LearnedRouteSaveMapSnapshot(
                    map.MapId ?? string.Empty,
                    map.LastTouchedUnixMilliseconds,
                    (map.Nodes ?? new List<LearnedRouteNodePersistenceModel>())
                        .Select(static node => new LearnedRouteSaveNodeSnapshot(
                            node.Id,
                            node.X,
                            node.Y,
                            node.Z,
                            node.HeightBand,
                            node.ConfidenceScore,
                            node.HasExactAnchor,
                            node.FirstSeenUnixMilliseconds,
                            node.LastSeenUnixMilliseconds,
                            node.ObservationCount))
                        .ToArray(),
                    (map.Edges ?? new List<LearnedRouteEdgePersistenceModel>())
                        .Select(static edge => new LearnedRouteSaveEdgeSnapshot(
                            edge.FromId,
                            edge.ToId,
                            edge.LengthMeters,
                            edge.ConfidenceScore,
                            edge.TraversalCount,
                            edge.LastTraversedUnixMilliseconds))
                        .ToArray()))
                .ToArray());
    }

    private static bool TryResolvePath(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(Path.GetDirectoryName(fullPath));
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static void CommitTemporaryFile(string temporaryPath, string destinationPath)
    {
        // Le temporaire se trouve toujours dans le même dossier. Replace et
        // Move sont donc une seule opération de publication atomique sur les
        // volumes Windows normaux.
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(
                    temporaryPath,
                    destinationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Certains dossiers virtualisés ou bacs à sable autorisent les
            // écritures mais refusent tout renommage. Le verrou par chemin
            // garantit encore un seul écrivain dans RaidPilot; ce repli garde
            // la compatibilité avec ces fournisseurs atypiques. En usage
            // normal, seul le chemin atomique ci-dessus est emprunté.
            File.Copy(temporaryPath, destinationPath, overwrite: true);
            File.Delete(temporaryPath);
        }
    }

    // Ces objets restent privés et leurs tableaux ne sont jamais exposés. Une
    // fois construits, ils forment donc l'instantané immuable donné au worker.
    private sealed record LearnedRouteSaveSnapshot(
        int SchemaVersion,
        IReadOnlyList<LearnedRouteSaveMapSnapshot> Maps);

    private sealed record LearnedRouteSaveMapSnapshot(
        string MapId,
        long LastTouchedUnixMilliseconds,
        IReadOnlyList<LearnedRouteSaveNodeSnapshot> Nodes,
        IReadOnlyList<LearnedRouteSaveEdgeSnapshot> Edges);

    private sealed record LearnedRouteSaveNodeSnapshot(
        long Id,
        double X,
        double Y,
        double Z,
        int HeightBand,
        double ConfidenceScore,
        bool HasExactAnchor,
        long FirstSeenUnixMilliseconds,
        long LastSeenUnixMilliseconds,
        int ObservationCount);

    private sealed record LearnedRouteSaveEdgeSnapshot(
        long FromId,
        long ToId,
        double LengthMeters,
        double ConfidenceScore,
        int TraversalCount,
        long LastTraversedUnixMilliseconds);
}

internal sealed class LearnedRoutePersistenceModel
{
    public int SchemaVersion { get; set; }

    public List<LearnedRouteMapPersistenceModel>? Maps { get; set; }
}

internal sealed class LearnedRouteMapPersistenceModel
{
    public string? MapId { get; set; }

    public long LastTouchedUnixMilliseconds { get; set; }

    public List<LearnedRouteNodePersistenceModel>? Nodes { get; set; }

    public List<LearnedRouteEdgePersistenceModel>? Edges { get; set; }
}

internal sealed class LearnedRouteNodePersistenceModel
{
    public long Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int HeightBand { get; set; }
    public double ConfidenceScore { get; set; }
    public bool HasExactAnchor { get; set; }
    public long FirstSeenUnixMilliseconds { get; set; }
    public long LastSeenUnixMilliseconds { get; set; }
    public int ObservationCount { get; set; }
}

internal sealed class LearnedRouteEdgePersistenceModel
{
    public long FromId { get; set; }
    public long ToId { get; set; }
    public double LengthMeters { get; set; }
    public double ConfidenceScore { get; set; }
    public int TraversalCount { get; set; }
    public long LastTraversedUnixMilliseconds { get; set; }
}
