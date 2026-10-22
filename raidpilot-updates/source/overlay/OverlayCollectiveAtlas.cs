using System.Diagnostics;
using System.Reflection;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly VisualKeyframeRecognizer _collectiveVisualKeyframeRecognizer = new();
    private readonly SuperKeyframeRecognizer _collectiveSuperKeyframeRecognizer = new();
    private readonly VisualRelocalizationEngineV2 _collectiveAtlasRelocalization = new();
    private readonly ToolStripMenuItem _collectiveAtlasShareMenuItem =
        new("Atlas collectif : partager mes repères anonymes")
        {
            CheckOnClick = true,
        };

    private CollectiveAtlasEngine? _collectiveAtlas;
    private CollectiveAtlasRelayClient? _collectiveAtlasRelay;
    private string _collectiveAtlasMap = string.Empty;
    private bool _collectiveAtlasShareEnabled;

    private void InitializeCollectiveAtlas()
    {
        var contributorId =
            CollectiveAtlasStore.GetOrCreateContributorId();
        _collectiveAtlas = new CollectiveAtlasEngine(
            contributorId,
            CollectiveAtlasStore.Load());
        _collectiveAtlasShareEnabled =
            CollectiveAtlasStore.LoadShareEnabled();

        _collectiveAtlasShareMenuItem.Checked =
            _collectiveAtlasShareEnabled;
        _collectiveAtlasShareMenuItem.Click += (_, _) =>
        {
            _collectiveAtlasShareEnabled =
                _collectiveAtlasShareMenuItem.Checked;
            CollectiveAtlasStore.SaveShareEnabled(
                _collectiveAtlasShareEnabled);

            if (_collectiveAtlasShareEnabled
                && _collectiveAtlasRelay is { } relay
                && _collectiveAtlas is { } atlas)
            {
                _ = relay.SendHelloAsync(
                    _formLifetime.Token);
                _ = SendCollectiveAtlasBatchAsync(
                    relay,
                    atlas.GetGossipBatch(
                        _collectiveAtlasMap,
                        12));
            }
        };

        if (_trayIcon.ContextMenuStrip is { } menu)
        {
            menu.Items.Insert(
                Math.Max(0, menu.Items.Count - 1),
                _collectiveAtlasShareMenuItem);
        }

        RebuildCollectiveAtlasRecognizers();
    }

    private void SwitchCollectiveAtlasMap(
        string mapId)
    {
        var normalized =
            CollectiveAtlasProtocol.NormalizeMapId(mapId);
        if (normalized.Length == 0
            || string.Equals(
                normalized,
                _collectiveAtlasMap,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var old = _collectiveAtlasRelay;
        _collectiveAtlasRelay = null;
        if (old is not null)
        {
            _ = old.DisposeAsync();
        }

        _collectiveAtlasMap = normalized;
        _collectiveAtlasRelocalization.BeginSession(
            Stopwatch.GetTimestamp());
        _collectiveVisualKeyframeRecognizer.ResetSession();
        _collectiveSuperKeyframeRecognizer.ResetSession();

        if (_collectiveAtlas is null)
        {
            return;
        }

        var relay = new CollectiveAtlasRelayClient(
            normalized,
            _collectiveAtlas.OwnContributorId);
        relay.CandidateReceived +=
            OnCollectiveAtlasCandidateReceived;
        relay.HelloReceived +=
            OnCollectiveAtlasHelloReceived;
        _collectiveAtlasRelay = relay;

        _ = StartCollectiveAtlasRelayAsync(relay);
    }

    private async Task StartCollectiveAtlasRelayAsync(
        CollectiveAtlasRelayClient relay)
    {
        try
        {
            await relay.StartReceivingAsync(
                _formLifetime.Token)
                .ConfigureAwait(false);
            await relay.SendHelloAsync(
                _formLifetime.Token)
                .ConfigureAwait(false);
            if (_collectiveAtlasShareEnabled
                && _collectiveAtlas is { } atlas)
            {
                await SendCollectiveAtlasBatchAsync(
                    relay,
                    atlas.GetGossipBatch(
                        _collectiveAtlasMap,
                        12))
                    .ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    private void OnCollectiveAtlasCandidateReceived(
        CollectiveAtlasCandidate candidate)
    {
        var atlas = _collectiveAtlas;
        if (atlas is null
            || !atlas.TryAdd(
                candidate,
                locallyValidated: false,
                out var changed)
            || !changed)
        {
            return;
        }

        CollectiveAtlasStore.Save(
            atlas.CreateSnapshot());
        RebuildCollectiveAtlasRecognizers();
    }

    private void OnCollectiveAtlasHelloReceived(
        string contributorId)
    {
        if (!_collectiveAtlasShareEnabled
            || _collectiveAtlas is not { } atlas
            || _collectiveAtlasRelay is not { } relay)
        {
            return;
        }

        var batch = atlas.GetGossipBatch(
            _collectiveAtlasMap,
            12);
        _ = SendCollectiveAtlasBatchAsync(
            relay,
            batch);
    }

    private async Task SendCollectiveAtlasBatchAsync(
        CollectiveAtlasRelayClient relay,
        IReadOnlyList<CollectiveAtlasCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (_formLifetime.IsCancellationRequested
                || !ReferenceEquals(
                    relay,
                    _collectiveAtlasRelay))
            {
                return;
            }

            try
            {
                await relay.SendCandidateAsync(
                    candidate,
                    _formLifetime.Token)
                    .ConfigureAwait(false);
                await Task.Delay(
                    80,
                    _formLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                return;
            }
        }
    }

    private void CollectiveAtlasObserveLocalExact(
        string mapId,
        VisualKeyframePose exactPose)
    {
        var atlas = _collectiveAtlas;
        if (atlas is null)
        {
            return;
        }

        var candidate =
            CollectiveAtlasEngine.TryCreateCandidate(
                atlas.OwnContributorId,
                mapId,
                exactPose,
                _visualKeyframeRecognizer.CreateSnapshot(),
                _superKeyframeRecognizer.CreateSnapshot(),
                Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString()
                    ?? "0");

        if (candidate is null
            || !atlas.TryAdd(
                candidate,
                locallyValidated: true,
                out var changed))
        {
            return;
        }

        if (changed)
        {
            CollectiveAtlasStore.Save(
                atlas.CreateSnapshot());
            RebuildCollectiveAtlasRecognizers();
        }

        if (_collectiveAtlasShareEnabled
            && _collectiveAtlasRelay is { } relay
            && string.Equals(
                candidate.MapId,
                _collectiveAtlasMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _ = relay.SendCandidateAsync(
                candidate,
                _formLifetime.Token);
        }
    }

    private void RebuildCollectiveAtlasRecognizers()
    {
        if (_collectiveAtlas is not { } atlas)
        {
            return;
        }

        var low = atlas.CreateVisualSnapshot(
            _collectiveVisualKeyframeRecognizer
                .CreateSnapshot());
        var high = atlas.CreateSuperSnapshot(
            _collectiveSuperKeyframeRecognizer
                .CreateSnapshot());

        _collectiveVisualKeyframeRecognizer
            .TryImportSnapshot(low);
        _collectiveSuperKeyframeRecognizer
            .TryImportSnapshot(high);
    }

    private bool CollectiveAtlasHasLowResolution(
        string mapId) =>
        _collectiveAtlas is { } atlas
        && atlas.ApprovedCountForMap(mapId) > 0;

    private bool CollectiveAtlasHasSuperKeyframes(
        string mapId) =>
        _collectiveSuperKeyframeRecognizer
            .KeyframeCountForMap(mapId) > 0;

    private VisualKeyframeRecognition
        ObserveCollectiveLow(
            string mapId,
            ReadOnlySpan<byte> visualFrame,
            VisualKeyframePose estimatedPose,
            long now,
            double uncertainty) =>
        _collectiveVisualKeyframeRecognizer.Observe(
            mapId,
            visualFrame,
            _visualMotionGate.SampleWidth,
            _visualMotionGate.SampleHeight,
            estimatedPose,
            now,
            uncertainty);

    private SuperKeyframeRecognition
        ObserveCollectiveSuper(
            string mapId,
            byte[] frame,
            VisualKeyframePose estimatedPose,
            long now,
            double uncertainty) =>
        _collectiveSuperKeyframeRecognizer.Observe(
            mapId,
            frame,
            estimatedPose,
            now,
            uncertainty);

    private void MarkCollectiveAtlasLocallyValidated(
        string mapId,
        VisualKeyframePose pose)
    {
        if (_collectiveAtlas is { } atlas
            && atlas.MarkLocallyValidated(
                mapId,
                pose))
        {
            CollectiveAtlasStore.Save(
                atlas.CreateSnapshot());
            RebuildCollectiveAtlasRecognizers();
        }
    }

    internal static bool
        ShouldAcceptCollectiveCorrection(
            VisualRelocalizationDecision decision,
            bool usedCollectiveEvidence) =>
        decision.ShouldCorrect
        && (!usedCollectiveEvidence
            || decision.Mode
                != VisualCorrectionMode.HardSnap);

    private void StopCollectiveAtlasRelay()
    {
        var relay = _collectiveAtlasRelay;
        _collectiveAtlasRelay = null;
        _collectiveAtlasMap = string.Empty;

        if (relay is not null)
        {
            _ = relay.DisposeAsync();
        }
    }

    private void DisposeCollectiveAtlas()
    {
        StopCollectiveAtlasRelay();

        if (_collectiveAtlas is { } atlas)
        {
            CollectiveAtlasStore.Save(
                atlas.CreateSnapshot());
        }
    }
}