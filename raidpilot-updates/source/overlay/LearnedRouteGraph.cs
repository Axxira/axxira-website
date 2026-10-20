namespace RaidPilot.Overlay;

/// <summary>
/// Origine d'une pose ajoutée au graphe de chemins appris.
/// </summary>
public enum LearnedPoseSource
{
    LiveEstimate,
    ExactAnchor,
}

/// <summary>
/// Pose horodatée utilisée pour apprendre un passage réellement parcouru.
/// </summary>
public readonly record struct LearnedRoutePose(
    string MapId,
    DateTimeOffset TimestampUtc,
    double X,
    double Y,
    double Z,
    LearnedPoseSource Source,
    double PositionUncertaintyMeters = 0.0);

public sealed record LearnedRouteTarget(
    string Id,
    string Name,
    double X,
    double Y,
    double Z);

public sealed record LearnedRouteGraphOptions
{
    public int MaxMaps { get; init; } = 16;

    public int MaxNodesPerMap { get; init; } = 4096;

    public int MaxEdgesPerMap { get; init; } = 16384;

    public double NodeSpacingMeters { get; init; } = 1.5;

    public double MergeHorizontalRadiusMeters { get; init; } = 1.25;

    public double MergeVerticalToleranceMeters { get; init; } = 1.1;

    public double HeightBandMeters { get; init; } = 2.5;

    public double MaximumSegmentHorizontalMeters { get; init; } = 8.0;

    public double MaximumSegmentVerticalMeters { get; init; } = 3.5;

    public TimeSpan MaximumSampleGap { get; init; } = TimeSpan.FromSeconds(2.0);

    public double MaximumLiveUncertaintyMeters { get; init; } = 12.0;

    public double StartSnapHorizontalRadiusMeters { get; init; } = 5.0;

    public double TargetSnapHorizontalRadiusMeters { get; init; } = 8.0;

    public double RouteVerticalToleranceMeters { get; init; } = 2.25;

    public double ArrivalHorizontalRadiusMeters { get; init; } = 5.0;
}

public enum LearnedRouteObservationStatus
{
    AddedNode,
    MergedNode,
    RejectedNoSession,
    RejectedWrongMap,
    RejectedInvalid,
    RejectedStale,
    RejectedUncertain,
    RejectedCapacity,
}

public readonly record struct LearnedRouteObservationResult(
    LearnedRouteObservationStatus Status,
    long? NodeId,
    bool AddedTraversedEdge)
{
    public bool Accepted => Status is LearnedRouteObservationStatus.AddedNode
        or LearnedRouteObservationStatus.MergedNode;
}

public enum LearnedRouteKind
{
    LearnedGraph,
    AlreadyAtTarget,
    DirectFallback,
}

public enum LearnedRouteConfidence
{
    Unknown,
    Low,
    Medium,
    High,
}

public enum LearnedRouteFallbackReason
{
    None,
    GraphEmpty,
    StartOutsideLearnedGraph,
    NoLearnedApproachToTarget,
    TargetOnDifferentHeight,
    LearnedComponentsDisconnected,
}

public sealed record LearnedRouteWaypoint(
    long NodeId,
    double X,
    double Y,
    double Z,
    int HeightBand,
    double ConfidenceScore,
    bool HasExactAnchor);

/// <summary>
/// Résultat d'un calcul de route. Un résultat <see cref="LearnedRouteKind.DirectFallback"/>
/// est explicitement non obstacle-aware et ne doit jamais être présenté comme un
/// chemin appris.
/// </summary>
public sealed record LearnedRoutePlan(
    LearnedRouteKind Kind,
    LearnedRouteTarget Target,
    IReadOnlyList<LearnedRouteWaypoint> Waypoints,
    double GraphDistanceMeters,
    double DirectDistanceMeters,
    double StartSnapDistanceMeters,
    double TargetSnapDistanceMeters,
    double ConfidenceScore,
    LearnedRouteConfidence Confidence,
    LearnedRouteFallbackReason FallbackReason,
    bool UsesOnlyTraversedEdges);

/// <summary>
/// Graphe local de chemins appris. Il n'ajoute une arête dirigée que lorsque
/// deux zones ont été reliées par une suite continue de poses observées dans
/// un même raid. La proximité seule ne crée jamais de passage.
/// </summary>
public sealed class LearnedRouteGraph
{
    private const double MinimumEdgeLengthMeters = 0.01;
    private const double MaximumAbsoluteCoordinateMeters = 1_000_000.0;

    private readonly object _sync = new();
    private readonly LearnedRouteGraphOptions _options;
    private readonly Dictionary<string, MapGraph> _maps = new(StringComparer.OrdinalIgnoreCase);
    private SessionState? _session;
    private long _nextNodeId = 1;

    public LearnedRouteGraph(LearnedRouteGraphOptions? options = null)
    {
        _options = options ?? new LearnedRouteGraphOptions();
        ValidateOptions(_options);
    }

    public LearnedRouteGraphOptions Options => _options;

    public void BeginSession(string mapId)
    {
        var normalizedMap = NormalizeMapId(mapId);
        if (normalizedMap is null)
        {
            throw new ArgumentException("La carte doit avoir un identifiant valide.", nameof(mapId));
        }

        lock (_sync)
        {
            var map = GetOrCreateMap(normalizedMap);
            map.LastTouchedUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _session = new SessionState(normalizedMap);
        }
    }

    public void EndSession()
    {
        lock (_sync)
        {
            _session = null;
        }
    }

    /// <summary>
    /// Coupe explicitement la continuité du trajet courant sans terminer le
    /// raid ni effacer les nœuds déjà appris. La prochaine observation
    /// amorce un nouveau segment et ne peut donc jamais créer une arête depuis
    /// la pose qui précédait la rupture.
    /// </summary>
    /// <remarks>
    /// Cette opération est idempotente et sûre même sans session active. Elle
    /// doit notamment être appelée avant d'injecter une correction absolue ou
    /// un recalage dont le saut ne représente pas un déplacement parcouru.
    /// </remarks>
    public void BreakContinuity()
    {
        lock (_sync)
        {
            BreakContinuityCore();
        }
    }

    public LearnedRouteObservationResult Observe(LearnedRoutePose pose)
    {
        lock (_sync)
        {
            if (_session is null)
            {
                return Rejected(LearnedRouteObservationStatus.RejectedNoSession);
            }

            var normalizedMap = NormalizeMapId(pose.MapId);
            if (normalizedMap is null
                || !string.Equals(normalizedMap, _session.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Rejected(LearnedRouteObservationStatus.RejectedWrongMap);
            }

            if (!IsValidPose(pose))
            {
                BreakContinuityCore();
                return Rejected(LearnedRouteObservationStatus.RejectedInvalid);
            }

            if (_session.LastTimestampUtc is { } lastTimestamp
                && pose.TimestampUtc <= lastTimestamp)
            {
                return Rejected(LearnedRouteObservationStatus.RejectedStale);
            }

            if (pose.Source == LearnedPoseSource.LiveEstimate
                && pose.PositionUncertaintyMeters > _options.MaximumLiveUncertaintyMeters)
            {
                BreakContinuityCore();
                _session.LastTimestampUtc = pose.TimestampUtc;
                return Rejected(LearnedRouteObservationStatus.RejectedUncertain);
            }

            var map = _maps[_session.MapId];
            var timestamp = pose.TimestampUtc.ToUnixTimeMilliseconds();
            map.LastTouchedUnixMilliseconds = timestamp;
            var poseConfidence = CalculatePoseConfidence(pose);

            var segmentIsContinuous = false;
            var segmentDistance = 0.0;
            if (_session.LastPose is { } previousPose)
            {
                var elapsed = pose.TimestampUtc - previousPose.TimestampUtc;
                var horizontal = HorizontalDistance(previousPose.X, previousPose.Z, pose.X, pose.Z);
                var vertical = Math.Abs(pose.Y - previousPose.Y);
                segmentIsContinuous = elapsed > TimeSpan.Zero
                    && elapsed <= _options.MaximumSampleGap
                    && horizontal <= _options.MaximumSegmentHorizontalMeters
                    && vertical <= _options.MaximumSegmentVerticalMeters;
                if (segmentIsContinuous)
                {
                    segmentDistance = Distance3D(previousPose.X, previousPose.Y, previousPose.Z, pose.X, pose.Y, pose.Z);
                }
            }

            if (!segmentIsContinuous && _session.LastPose.HasValue)
            {
                _session.CurrentNodeId = null;
                _session.DistanceSinceNodeMeters = 0.0;
                _session.PathConfidence = 1.0;
            }

            if (segmentIsContinuous)
            {
                _session.DistanceSinceNodeMeters += segmentDistance;
                _session.PathConfidence = Math.Min(_session.PathConfidence, poseConfidence);
            }
            else
            {
                _session.PathConfidence = poseConfidence;
            }

            var matched = FindNearestMergeNode(map, pose);
            if (matched is null
                && _session.CurrentNodeId is { } currentNodeId
                && map.Nodes.TryGetValue(currentNodeId, out var currentNode)
                && Math.Abs(currentNode.Y - pose.Y) <= _options.MergeVerticalToleranceMeters
                && Distance3D(currentNode.X, currentNode.Y, currentNode.Z, pose.X, pose.Y, pose.Z)
                    < _options.NodeSpacingMeters)
            {
                matched = currentNode;
            }

            var addedNode = false;
            if (matched is null)
            {
                if (!EnsureNodeCapacity(map, _session.CurrentNodeId))
                {
                    BreakContinuityCore();
                    return Rejected(LearnedRouteObservationStatus.RejectedCapacity);
                }

                if (_nextNodeId <= 0 || _nextNodeId == long.MaxValue)
                {
                    BreakContinuityCore();
                    return Rejected(LearnedRouteObservationStatus.RejectedCapacity);
                }

                matched = new GraphNode(
                    _nextNodeId++,
                    pose.X,
                    pose.Y,
                    pose.Z,
                    HeightBandFor(pose.Y),
                    poseConfidence,
                    pose.Source == LearnedPoseSource.ExactAnchor,
                    timestamp,
                    timestamp,
                    1);
                map.Nodes.Add(matched.Id, matched);
                addedNode = true;
            }
            else
            {
                matched.ConfidenceScore = Math.Max(matched.ConfidenceScore, poseConfidence);
                matched.HasExactAnchor |= pose.Source == LearnedPoseSource.ExactAnchor;
                matched.LastSeenUnixMilliseconds = Math.Max(matched.LastSeenUnixMilliseconds, timestamp);
                if (matched.ObservationCount < int.MaxValue)
                {
                    matched.ObservationCount++;
                }
            }

            var edgeAdded = false;
            if (segmentIsContinuous
                && _session.CurrentNodeId is { } fromId
                && fromId != matched.Id
                && map.Nodes.ContainsKey(fromId))
            {
                var pathDistance = Math.Max(
                    _session.DistanceSinceNodeMeters,
                    Distance3D(
                        map.Nodes[fromId].X,
                        map.Nodes[fromId].Y,
                        map.Nodes[fromId].Z,
                        matched.X,
                        matched.Y,
                        matched.Z));
                if (pathDistance >= MinimumEdgeLengthMeters)
                {
                    edgeAdded = AddOrUpdateTraversedEdge(
                        map,
                        fromId,
                        matched.Id,
                        pathDistance,
                        Math.Min(
                            _session.PathConfidence,
                            Math.Min(map.Nodes[fromId].ConfidenceScore, matched.ConfidenceScore)),
                        timestamp);
                }
            }

            if (_session.CurrentNodeId != matched.Id)
            {
                _session.DistanceSinceNodeMeters = 0.0;
                _session.PathConfidence = poseConfidence;
            }

            _session.CurrentNodeId = matched.Id;
            _session.LastPose = pose;
            _session.LastTimestampUtc = pose.TimestampUtc;

            return new LearnedRouteObservationResult(
                addedNode ? LearnedRouteObservationStatus.AddedNode : LearnedRouteObservationStatus.MergedNode,
                matched.Id,
                edgeAdded);
        }
    }

    public bool TryPlanRoute(
        LearnedRoutePose currentPose,
        IEnumerable<LearnedRouteTarget> targets,
        out LearnedRoutePlan? plan,
        bool allowDirectFallback = true)
    {
        ArgumentNullException.ThrowIfNull(targets);
        plan = null;
        if (!IsValidPose(currentPose))
        {
            return false;
        }
        var currentPoseConfidence = CalculatePoseConfidence(currentPose);

        var targetArray = targets
            .Where(IsValidTarget)
            .GroupBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetArray.Length == 0)
        {
            return false;
        }

        var arrived = targetArray
            .Select(target => new TargetDistance(target, DistanceTo(currentPose, target)))
            .Where(item => item.Distance.Horizontal <= _options.ArrivalHorizontalRadiusMeters
                && item.Distance.Vertical <= _options.RouteVerticalToleranceMeters)
            .OrderBy(item => item.Distance.ThreeDimensional)
            .ThenBy(item => item.Target.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (arrived is not null)
        {
            plan = new LearnedRoutePlan(
                LearnedRouteKind.AlreadyAtTarget,
                arrived.Target,
                Array.Empty<LearnedRouteWaypoint>(),
                0.0,
                arrived.Distance.ThreeDimensional,
                0.0,
                0.0,
                currentPoseConfidence,
                ConfidenceFor(currentPoseConfidence),
                LearnedRouteFallbackReason.None,
                true);
            return true;
        }

        var normalizedMap = NormalizeMapId(currentPose.MapId);
        lock (_sync)
        {
            if (normalizedMap is not null
                && _maps.TryGetValue(normalizedMap, out var map)
                && map.Nodes.Count > 0)
            {
                var start = FindNearestRouteNode(map, currentPose);
                if (start is not null)
                {
                    LearnedRoutePlan? best = null;
                    var hasAnyApproach = false;
                    var hasOtherHeightApproach = false;
                    foreach (var target in targetArray)
                    {
                        var goalNodes = map.Nodes.Values
                            .Where(node =>
                            {
                                var horizontal = HorizontalDistance(node.X, node.Z, target.X, target.Z);
                                var vertical = Math.Abs(node.Y - target.Y);
                                if (horizontal <= _options.TargetSnapHorizontalRadiusMeters
                                    && vertical > _options.RouteVerticalToleranceMeters)
                                {
                                    hasOtherHeightApproach = true;
                                }

                                return horizontal <= _options.TargetSnapHorizontalRadiusMeters
                                    && vertical <= _options.RouteVerticalToleranceMeters;
                            })
                            .OrderBy(node => Distance3D(node.X, node.Y, node.Z, target.X, target.Y, target.Z))
                            .ThenBy(node => node.Id)
                            .ToArray();
                        if (goalNodes.Length == 0)
                        {
                            continue;
                        }

                        hasAnyApproach = true;
                        if (!TryFindShortestPath(map, start, goalNodes, out var path, out var graphDistance))
                        {
                            continue;
                        }

                        var goal = path[^1];
                        var startGap = Distance3D(currentPose.X, currentPose.Y, currentPose.Z, start.X, start.Y, start.Z);
                        var targetGap = Distance3D(goal.X, goal.Y, goal.Z, target.X, target.Y, target.Z);
                        var score = Math.Min(
                            currentPoseConfidence,
                            CalculateRouteConfidence(map, path, startGap, targetGap));
                        var candidate = new LearnedRoutePlan(
                            LearnedRouteKind.LearnedGraph,
                            target,
                            path.Select(ToWaypoint).ToArray(),
                            graphDistance,
                            DistanceTo(currentPose, target).ThreeDimensional,
                            startGap,
                            targetGap,
                            score,
                            ConfidenceFor(score),
                            LearnedRouteFallbackReason.None,
                            true);

                        if (best is null
                            || TotalLearnedDistance(candidate) < TotalLearnedDistance(best) - 0.000001
                            || (Math.Abs(TotalLearnedDistance(candidate) - TotalLearnedDistance(best)) <= 0.000001
                                && string.Compare(candidate.Target.Id, best.Target.Id, StringComparison.OrdinalIgnoreCase) < 0))
                        {
                            best = candidate;
                        }
                    }

                    if (best is not null)
                    {
                        plan = best;
                        return true;
                    }

                    var reason = hasAnyApproach
                        ? LearnedRouteFallbackReason.LearnedComponentsDisconnected
                        : hasOtherHeightApproach
                            ? LearnedRouteFallbackReason.TargetOnDifferentHeight
                            : LearnedRouteFallbackReason.NoLearnedApproachToTarget;
                    return TryCreateFallback(currentPose, targetArray, reason, allowDirectFallback, out plan);
                }

                return TryCreateFallback(
                    currentPose,
                    targetArray,
                    LearnedRouteFallbackReason.StartOutsideLearnedGraph,
                    allowDirectFallback,
                    out plan);
            }

            return TryCreateFallback(
                currentPose,
                targetArray,
                LearnedRouteFallbackReason.GraphEmpty,
                allowDirectFallback,
                out plan);
        }
    }

    public int GetNodeCount(string mapId)
    {
        var normalized = NormalizeMapId(mapId);
        lock (_sync)
        {
            return normalized is not null && _maps.TryGetValue(normalized, out var map)
                ? map.Nodes.Count
                : 0;
        }
    }

    public int GetEdgeCount(string mapId)
    {
        var normalized = NormalizeMapId(mapId);
        lock (_sync)
        {
            return normalized is not null && _maps.TryGetValue(normalized, out var map)
                ? map.Edges.Count
                : 0;
        }
    }

    internal LearnedRoutePersistenceModel CreatePersistenceModel()
    {
        lock (_sync)
        {
            return new LearnedRoutePersistenceModel
            {
                SchemaVersion = LearnedRouteGraphStore.SchemaVersion,
                Maps = _maps.Values
                    .OrderBy(map => map.MapId, StringComparer.OrdinalIgnoreCase)
                    .Select(map => new LearnedRouteMapPersistenceModel
                    {
                        MapId = map.MapId,
                        LastTouchedUnixMilliseconds = map.LastTouchedUnixMilliseconds,
                        Nodes = map.Nodes.Values
                            .OrderBy(node => node.Id)
                            .Select(node => new LearnedRouteNodePersistenceModel
                            {
                                Id = node.Id,
                                X = node.X,
                                Y = node.Y,
                                Z = node.Z,
                                HeightBand = node.HeightBand,
                                ConfidenceScore = node.ConfidenceScore,
                                HasExactAnchor = node.HasExactAnchor,
                                FirstSeenUnixMilliseconds = node.FirstSeenUnixMilliseconds,
                                LastSeenUnixMilliseconds = node.LastSeenUnixMilliseconds,
                                ObservationCount = node.ObservationCount,
                            })
                            .ToList(),
                        Edges = map.Edges.Values
                            .OrderBy(edge => edge.FromId)
                            .ThenBy(edge => edge.ToId)
                            .Select(edge => new LearnedRouteEdgePersistenceModel
                            {
                                FromId = edge.FromId,
                                ToId = edge.ToId,
                                LengthMeters = edge.LengthMeters,
                                ConfidenceScore = edge.ConfidenceScore,
                                TraversalCount = edge.TraversalCount,
                                LastTraversedUnixMilliseconds = edge.LastTraversedUnixMilliseconds,
                            })
                            .ToList(),
                    })
                    .ToList(),
            };
        }
    }

    internal static bool TryCreateFromPersistenceModel(
        LearnedRoutePersistenceModel? model,
        LearnedRouteGraphOptions? options,
        out LearnedRouteGraph? graph)
    {
        graph = null;
        LearnedRouteGraph candidate;
        try
        {
            candidate = new LearnedRouteGraph(options);
        }
        catch
        {
            return false;
        }

        if (model is null
            || model.SchemaVersion != LearnedRouteGraphStore.SchemaVersion
            || model.Maps is null
            || model.Maps.Count > candidate._options.MaxMaps)
        {
            return false;
        }

        var maxNodeId = 0L;
        foreach (var persistedMap in model.Maps)
        {
            var mapId = NormalizeMapId(persistedMap.MapId);
            if (mapId is null
                || persistedMap.Nodes is null
                || persistedMap.Edges is null
                || persistedMap.Nodes.Count > candidate._options.MaxNodesPerMap
                || persistedMap.Edges.Count > candidate._options.MaxEdgesPerMap
                || candidate._maps.ContainsKey(mapId))
            {
                return false;
            }

            var map = new MapGraph(mapId)
            {
                LastTouchedUnixMilliseconds = persistedMap.LastTouchedUnixMilliseconds,
            };
            foreach (var persistedNode in persistedMap.Nodes)
            {
                if (persistedNode.Id <= 0
                    || persistedNode.Id == long.MaxValue
                    || !AreFinite(
                        persistedNode.X,
                        persistedNode.Y,
                        persistedNode.Z,
                        persistedNode.ConfidenceScore)
                    || Math.Abs(persistedNode.X) > MaximumAbsoluteCoordinateMeters
                    || Math.Abs(persistedNode.Y) > MaximumAbsoluteCoordinateMeters
                    || Math.Abs(persistedNode.Z) > MaximumAbsoluteCoordinateMeters
                    || persistedNode.ConfidenceScore is < 0.0 or > 1.0
                    || persistedNode.ObservationCount <= 0
                    || persistedNode.FirstSeenUnixMilliseconds > persistedNode.LastSeenUnixMilliseconds
                    || persistedNode.HeightBand != candidate.HeightBandFor(persistedNode.Y)
                    || map.Nodes.ContainsKey(persistedNode.Id))
                {
                    return false;
                }

                map.Nodes.Add(persistedNode.Id, new GraphNode(
                    persistedNode.Id,
                    persistedNode.X,
                    persistedNode.Y,
                    persistedNode.Z,
                    persistedNode.HeightBand,
                    persistedNode.ConfidenceScore,
                    persistedNode.HasExactAnchor,
                    persistedNode.FirstSeenUnixMilliseconds,
                    persistedNode.LastSeenUnixMilliseconds,
                    persistedNode.ObservationCount));
                maxNodeId = Math.Max(maxNodeId, persistedNode.Id);
            }

            foreach (var persistedEdge in persistedMap.Edges)
            {
                var key = new EdgeKey(persistedEdge.FromId, persistedEdge.ToId);
                if (persistedEdge.FromId == persistedEdge.ToId
                    || !map.Nodes.ContainsKey(persistedEdge.FromId)
                    || !map.Nodes.ContainsKey(persistedEdge.ToId)
                    || !AreFinite(persistedEdge.LengthMeters, persistedEdge.ConfidenceScore)
                    || persistedEdge.LengthMeters < MinimumEdgeLengthMeters
                    || persistedEdge.ConfidenceScore is < 0.0 or > 1.0
                    || persistedEdge.TraversalCount <= 0
                    || map.Edges.ContainsKey(key))
                {
                    return false;
                }

                var directDistance = Distance3D(
                    map.Nodes[persistedEdge.FromId].X,
                    map.Nodes[persistedEdge.FromId].Y,
                    map.Nodes[persistedEdge.FromId].Z,
                    map.Nodes[persistedEdge.ToId].X,
                    map.Nodes[persistedEdge.ToId].Y,
                    map.Nodes[persistedEdge.ToId].Z);
                if (persistedEdge.LengthMeters + 0.001 < directDistance)
                {
                    return false;
                }

                map.Edges.Add(key, new GraphEdge(
                    persistedEdge.FromId,
                    persistedEdge.ToId,
                    persistedEdge.LengthMeters,
                    persistedEdge.ConfidenceScore,
                    persistedEdge.TraversalCount,
                    persistedEdge.LastTraversedUnixMilliseconds));
            }

            candidate._maps.Add(mapId, map);
        }

        candidate._nextNodeId = maxNodeId == long.MaxValue ? long.MaxValue : maxNodeId + 1;
        graph = candidate;
        return true;
    }

    private static LearnedRouteObservationResult Rejected(LearnedRouteObservationStatus status) =>
        new(status, null, false);

    private void BreakContinuityCore()
    {
        if (_session is null)
        {
            return;
        }

        _session.CurrentNodeId = null;
        _session.LastPose = null;
        _session.LastTimestampUtc = null;
        _session.DistanceSinceNodeMeters = 0.0;
        _session.PathConfidence = 1.0;
    }

    private MapGraph GetOrCreateMap(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var existing))
        {
            return existing;
        }

        if (_maps.Count >= _options.MaxMaps)
        {
            var oldest = _maps.Values
                .OrderBy(map => map.LastTouchedUnixMilliseconds)
                .ThenBy(map => map.MapId, StringComparer.OrdinalIgnoreCase)
                .First();
            _maps.Remove(oldest.MapId);
        }

        var created = new MapGraph(mapId);
        _maps.Add(mapId, created);
        return created;
    }

    private bool EnsureNodeCapacity(MapGraph map, long? protectedNodeId)
    {
        if (map.Nodes.Count < _options.MaxNodesPerMap)
        {
            return true;
        }

        var evicted = map.Nodes.Values
            .Where(node => node.Id != protectedNodeId)
            .OrderBy(node => node.HasExactAnchor)
            .ThenBy(node => node.ObservationCount)
            .ThenBy(node => node.LastSeenUnixMilliseconds)
            .ThenBy(node => node.Id)
            .FirstOrDefault();
        if (evicted is null)
        {
            return false;
        }

        map.Nodes.Remove(evicted.Id);
        foreach (var key in map.Edges.Keys
            .Where(key => key.FromId == evicted.Id || key.ToId == evicted.Id)
            .ToArray())
        {
            map.Edges.Remove(key);
        }

        return true;
    }

    private GraphNode? FindNearestMergeNode(MapGraph map, LearnedRoutePose pose)
    {
        GraphNode? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var node in map.Nodes.Values)
        {
            var vertical = Math.Abs(node.Y - pose.Y);
            if (vertical > _options.MergeVerticalToleranceMeters)
            {
                continue;
            }

            var horizontal = HorizontalDistance(node.X, node.Z, pose.X, pose.Z);
            if (horizontal > _options.MergeHorizontalRadiusMeters)
            {
                continue;
            }

            var distance = Math.Sqrt((horizontal * horizontal) + (vertical * vertical));
            if (distance < bestDistance
                || (Math.Abs(distance - bestDistance) <= 0.000001 && node.Id < best?.Id))
            {
                best = node;
                bestDistance = distance;
            }
        }

        return best;
    }

    private GraphNode? FindNearestRouteNode(MapGraph map, LearnedRoutePose pose)
    {
        return map.Nodes.Values
            .Select(node => new
            {
                Node = node,
                Horizontal = HorizontalDistance(node.X, node.Z, pose.X, pose.Z),
                Vertical = Math.Abs(node.Y - pose.Y),
            })
            .Where(item => item.Horizontal <= _options.StartSnapHorizontalRadiusMeters
                && item.Vertical <= _options.RouteVerticalToleranceMeters)
            .OrderBy(item => Math.Sqrt((item.Horizontal * item.Horizontal) + (item.Vertical * item.Vertical)))
            .ThenBy(item => item.Node.Id)
            .Select(item => item.Node)
            .FirstOrDefault();
    }

    private bool AddOrUpdateTraversedEdge(
        MapGraph map,
        long fromId,
        long toId,
        double lengthMeters,
        double confidence,
        long timestamp)
    {
        var key = new EdgeKey(fromId, toId);
        if (map.Edges.TryGetValue(key, out var existing))
        {
            existing.LengthMeters = Math.Min(existing.LengthMeters, lengthMeters);
            existing.ConfidenceScore = Math.Max(existing.ConfidenceScore, confidence);
            if (existing.TraversalCount < int.MaxValue)
            {
                existing.TraversalCount++;
            }
            existing.LastTraversedUnixMilliseconds = Math.Max(existing.LastTraversedUnixMilliseconds, timestamp);
            return true;
        }

        if (map.Edges.Count >= _options.MaxEdgesPerMap)
        {
            var evicted = map.Edges.Values
                .OrderBy(edge => edge.TraversalCount)
                .ThenBy(edge => edge.ConfidenceScore)
                .ThenBy(edge => edge.LastTraversedUnixMilliseconds)
                .ThenBy(edge => edge.FromId)
                .ThenBy(edge => edge.ToId)
                .First();
            map.Edges.Remove(new EdgeKey(evicted.FromId, evicted.ToId));
        }

        map.Edges.Add(key, new GraphEdge(fromId, toId, lengthMeters, confidence, 1, timestamp));
        return true;
    }

    private static bool TryFindShortestPath(
        MapGraph map,
        GraphNode start,
        IReadOnlyCollection<GraphNode> goals,
        out IReadOnlyList<GraphNode> path,
        out double distanceMeters)
    {
        path = Array.Empty<GraphNode>();
        distanceMeters = 0.0;
        var goalIds = goals.Select(goal => goal.Id).ToHashSet();
        if (goalIds.Contains(start.Id))
        {
            path = new[] { start };
            return true;
        }

        var outgoing = map.Edges.Values
            .GroupBy(edge => edge.FromId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => edge.ToId).ToArray());
        var distances = new Dictionary<long, double> { [start.Id] = 0.0 };
        var previous = new Dictionary<long, long>();
        var queue = new PriorityQueue<(long NodeId, double Distance), double>();
        queue.Enqueue((start.Id, 0.0), Heuristic(start, goals));

        long? reachedGoal = null;
        while (queue.TryDequeue(out var state, out _))
        {
            if (!distances.TryGetValue(state.NodeId, out var knownDistance)
                || state.Distance > knownDistance + 0.000001)
            {
                continue;
            }

            if (goalIds.Contains(state.NodeId))
            {
                reachedGoal = state.NodeId;
                break;
            }

            if (!outgoing.TryGetValue(state.NodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (!map.Nodes.TryGetValue(edge.ToId, out var next))
                {
                    continue;
                }

                var candidateDistance = knownDistance + edge.LengthMeters;
                if (distances.TryGetValue(next.Id, out var oldDistance)
                    && candidateDistance >= oldDistance - 0.000001)
                {
                    continue;
                }

                distances[next.Id] = candidateDistance;
                previous[next.Id] = state.NodeId;
                queue.Enqueue(
                    (next.Id, candidateDistance),
                    candidateDistance + Heuristic(next, goals));
            }
        }

        if (reachedGoal is not { } goalId)
        {
            return false;
        }

        var ids = new List<long> { goalId };
        while (ids[^1] != start.Id)
        {
            if (!previous.TryGetValue(ids[^1], out var previousId))
            {
                return false;
            }

            ids.Add(previousId);
        }

        ids.Reverse();
        path = ids.Select(id => map.Nodes[id]).ToArray();
        distanceMeters = distances[goalId];
        return true;
    }

    private double CalculateRouteConfidence(
        MapGraph map,
        IReadOnlyList<GraphNode> path,
        double startGap,
        double targetGap)
    {
        var score = path.Min(node => node.ConfidenceScore);
        for (var index = 1; index < path.Count; index++)
        {
            if (map.Edges.TryGetValue(new EdgeKey(path[index - 1].Id, path[index].Id), out var edge))
            {
                score = Math.Min(score, edge.ConfidenceScore);
            }
        }

        var snapPenalty = Math.Min(
            0.35,
            (0.20 * startGap / Math.Max(0.001, _options.StartSnapHorizontalRadiusMeters))
                + (0.15 * targetGap / Math.Max(0.001, _options.TargetSnapHorizontalRadiusMeters)));
        return Math.Clamp(score * (1.0 - snapPenalty), 0.0, 1.0);
    }

    private static double Heuristic(GraphNode node, IReadOnlyCollection<GraphNode> goals) =>
        goals.Min(goal => Distance3D(node.X, node.Y, node.Z, goal.X, goal.Y, goal.Z));

    private static LearnedRouteWaypoint ToWaypoint(GraphNode node) => new(
        node.Id,
        node.X,
        node.Y,
        node.Z,
        node.HeightBand,
        node.ConfidenceScore,
        node.HasExactAnchor);

    private static double TotalLearnedDistance(LearnedRoutePlan plan) =>
        plan.StartSnapDistanceMeters + plan.GraphDistanceMeters + plan.TargetSnapDistanceMeters;

    private bool TryCreateFallback(
        LearnedRoutePose pose,
        IReadOnlyList<LearnedRouteTarget> targets,
        LearnedRouteFallbackReason reason,
        bool allowDirectFallback,
        out LearnedRoutePlan? plan)
    {
        plan = null;
        if (!allowDirectFallback)
        {
            return false;
        }

        var nearest = targets
            .Select(target => new TargetDistance(target, DistanceTo(pose, target)))
            .OrderBy(item => item.Distance.ThreeDimensional)
            .ThenBy(item => item.Target.Id, StringComparer.OrdinalIgnoreCase)
            .First();
        plan = new LearnedRoutePlan(
            LearnedRouteKind.DirectFallback,
            nearest.Target,
            Array.Empty<LearnedRouteWaypoint>(),
            0.0,
            nearest.Distance.ThreeDimensional,
            0.0,
            0.0,
            0.0,
            LearnedRouteConfidence.Unknown,
            reason,
            false);
        return true;
    }

    private double CalculatePoseConfidence(LearnedRoutePose pose)
    {
        if (pose.Source == LearnedPoseSource.ExactAnchor)
        {
            return 1.0;
        }

        return Math.Clamp(
            1.0 - (pose.PositionUncertaintyMeters / Math.Max(0.001, _options.MaximumLiveUncertaintyMeters)),
            0.05,
            0.95);
    }

    private int HeightBandFor(double y) => checked((int)Math.Floor(y / _options.HeightBandMeters));

    private static LearnedRouteConfidence ConfidenceFor(double score) => score switch
    {
        >= 0.80 => LearnedRouteConfidence.High,
        >= 0.45 => LearnedRouteConfidence.Medium,
        > 0.0 => LearnedRouteConfidence.Low,
        _ => LearnedRouteConfidence.Unknown,
    };

    private static bool IsValidPose(LearnedRoutePose pose) =>
        NormalizeMapId(pose.MapId) is not null
        && pose.TimestampUtc != default
        && Enum.IsDefined(pose.Source)
        && AreFinite(pose.X, pose.Y, pose.Z, pose.PositionUncertaintyMeters)
        && Math.Abs(pose.X) <= MaximumAbsoluteCoordinateMeters
        && Math.Abs(pose.Y) <= MaximumAbsoluteCoordinateMeters
        && Math.Abs(pose.Z) <= MaximumAbsoluteCoordinateMeters
        && pose.PositionUncertaintyMeters >= 0.0;

    private static bool IsValidTarget(LearnedRouteTarget target) =>
        target is not null
        && !string.IsNullOrWhiteSpace(target.Id)
        && target.Id.Length <= 160
        && !string.IsNullOrWhiteSpace(target.Name)
        && target.Name.Length <= 240
        && AreFinite(target.X, target.Y, target.Z)
        && Math.Abs(target.X) <= MaximumAbsoluteCoordinateMeters
        && Math.Abs(target.Y) <= MaximumAbsoluteCoordinateMeters
        && Math.Abs(target.Z) <= MaximumAbsoluteCoordinateMeters;

    private static string? NormalizeMapId(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        var normalized = mapId.Trim().ToLowerInvariant();
        return normalized.Length <= 80 ? normalized : null;
    }

    private static DistanceComponents DistanceTo(LearnedRoutePose pose, LearnedRouteTarget target)
    {
        var horizontal = HorizontalDistance(pose.X, pose.Z, target.X, target.Z);
        var vertical = Math.Abs(pose.Y - target.Y);
        return new DistanceComponents(horizontal, vertical, Math.Sqrt((horizontal * horizontal) + (vertical * vertical)));
    }

    private static double HorizontalDistance(double x1, double z1, double x2, double z2)
    {
        var dx = x2 - x1;
        var dz = z2 - z1;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private static double Distance3D(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        var horizontal = HorizontalDistance(x1, z1, x2, z2);
        var vertical = y2 - y1;
        return Math.Sqrt((horizontal * horizontal) + (vertical * vertical));
    }

    private static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    private static void ValidateOptions(LearnedRouteGraphOptions options)
    {
        if (options.MaxMaps <= 0
            || options.MaxNodesPerMap <= 0
            || options.MaxEdgesPerMap <= 0
            || options.MaximumSampleGap <= TimeSpan.Zero
            || !AreFinite(
                options.NodeSpacingMeters,
                options.MergeHorizontalRadiusMeters,
                options.MergeVerticalToleranceMeters,
                options.HeightBandMeters,
                options.MaximumSegmentHorizontalMeters,
                options.MaximumSegmentVerticalMeters,
                options.MaximumLiveUncertaintyMeters,
                options.StartSnapHorizontalRadiusMeters,
                options.TargetSnapHorizontalRadiusMeters,
                options.RouteVerticalToleranceMeters,
                options.ArrivalHorizontalRadiusMeters)
            || options.NodeSpacingMeters <= 0.0
            || options.MergeHorizontalRadiusMeters <= 0.0
            || options.MergeVerticalToleranceMeters <= 0.0
            || options.HeightBandMeters < 0.1
            || options.MaximumSegmentHorizontalMeters <= 0.0
            || options.MaximumSegmentVerticalMeters <= 0.0
            || options.MaximumLiveUncertaintyMeters <= 0.0
            || options.StartSnapHorizontalRadiusMeters <= 0.0
            || options.TargetSnapHorizontalRadiusMeters <= 0.0
            || options.RouteVerticalToleranceMeters <= 0.0
            || options.ArrivalHorizontalRadiusMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Les limites du graphe doivent être finies et positives.");
        }
    }

    private sealed class SessionState(string mapId)
    {
        public string MapId { get; } = mapId;

        public long? CurrentNodeId { get; set; }

        public LearnedRoutePose? LastPose { get; set; }

        public DateTimeOffset? LastTimestampUtc { get; set; }

        public double DistanceSinceNodeMeters { get; set; }

        public double PathConfidence { get; set; } = 1.0;
    }

    private sealed class MapGraph(string mapId)
    {
        public string MapId { get; } = mapId;

        public long LastTouchedUnixMilliseconds { get; set; }

        public Dictionary<long, GraphNode> Nodes { get; } = new();

        public Dictionary<EdgeKey, GraphEdge> Edges { get; } = new();
    }

    private sealed class GraphNode(
        long id,
        double x,
        double y,
        double z,
        int heightBand,
        double confidenceScore,
        bool hasExactAnchor,
        long firstSeenUnixMilliseconds,
        long lastSeenUnixMilliseconds,
        int observationCount)
    {
        public long Id { get; } = id;
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Z { get; } = z;
        public int HeightBand { get; } = heightBand;
        public double ConfidenceScore { get; set; } = confidenceScore;
        public bool HasExactAnchor { get; set; } = hasExactAnchor;
        public long FirstSeenUnixMilliseconds { get; } = firstSeenUnixMilliseconds;
        public long LastSeenUnixMilliseconds { get; set; } = lastSeenUnixMilliseconds;
        public int ObservationCount { get; set; } = observationCount;
    }

    private sealed class GraphEdge(
        long fromId,
        long toId,
        double lengthMeters,
        double confidenceScore,
        int traversalCount,
        long lastTraversedUnixMilliseconds)
    {
        public long FromId { get; } = fromId;
        public long ToId { get; } = toId;
        public double LengthMeters { get; set; } = lengthMeters;
        public double ConfidenceScore { get; set; } = confidenceScore;
        public int TraversalCount { get; set; } = traversalCount;
        public long LastTraversedUnixMilliseconds { get; set; } = lastTraversedUnixMilliseconds;
    }

    private readonly record struct EdgeKey(long FromId, long ToId);

    private sealed record TargetDistance(LearnedRouteTarget Target, DistanceComponents Distance);

    private readonly record struct DistanceComponents(double Horizontal, double Vertical, double ThreeDimensional);
}