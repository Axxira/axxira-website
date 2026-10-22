namespace RaidPilot.Overlay.Tests;

internal static class LocalPlayerMarkerTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("point local indépendant du partage tarkov.dev", PublishesLocalMarkerWithoutRemoteState, failures);
        Run("point local refuse une ancienne génération", RejectsStaleGeneration, failures);
        Run("point local refuse une autre carte", RejectsWrongMap, failures);
        Run("point local possède son propre rendu Leaflet", HasIndependentLeafletRenderer, failures);
        Run("point local projette le cap à travers Leaflet", AppliesMapCoordinateRotation, failures);
        Run("point local reste centré et la carte suit", KeepsPlayerCenteredWhileMapMoves, failures);
    }

    private static void PublishesLocalMarkerWithoutRemoteState()
    {
        Require(
            OverlayForm.ShouldPublishLocalPlayerMarker(
                webReady: true,
                coreReady: true,
                updateRaidGeneration: 12,
                currentRaidGeneration: 12,
                updateMap: "the-lab",
                currentMap: "the-lab"),
            "le point local dépend encore d'un état GPS distant");
    }

    private static void RejectsStaleGeneration()
    {
        Require(
            !OverlayForm.ShouldPublishLocalPlayerMarker(
                true, true, 11, 12, "the-lab", "the-lab"),
            "une pose d'un ancien raid peut encore être dessinée");
        Require(
            !OverlayForm.ShouldPublishLocalPlayerMarker(
                false, true, 12, 12, "the-lab", "the-lab"),
            "le rendu part avant que la page soit prête");
    }

    private static void RejectsWrongMap()
    {
        Require(
            !OverlayForm.ShouldPublishLocalPlayerMarker(
                true, true, 12, 12, "customs", "the-lab"),
            "une pose d'une autre carte peut encore être dessinée");
        Require(
            !OverlayForm.ShouldPublishLocalPlayerMarker(
                true, false, 12, 12, "the-lab", "the-lab"),
            "le rendu tente d'utiliser un WebView absent");
    }

    private static void HasIndependentLeafletRenderer()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css"));

        Require(
            behavior.Contains("__raidPilotSetLocalPose", StringComparison.Ordinal)
            && behavior.Contains("__raidPilotClearLocalPose", StringComparison.Ordinal)
            && behavior.Contains("latLngToContainerPoint([pose.z, pose.x])", StringComparison.Ordinal)
            && behavior.Contains("localPlayerPoseState", StringComparison.Ordinal)
            && styles.Contains("#raidpilot-local-player-marker", StringComparison.Ordinal),
            "le marqueur local n'est pas autonome dans la page");
    }

    private static void AppliesMapCoordinateRotation()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        Require(
            behavior.Contains("raidPilotProjectedDisplayYaw", StringComparison.Ordinal)
            && behavior.Contains("forwardX = x + Math.sin(radians)", StringComparison.Ordinal)
            && behavior.Contains("forwardZ = z + Math.cos(radians)", StringComparison.Ordinal)
            && behavior.Contains("Math.atan2(dx, -dy)", StringComparison.Ordinal)
            && behavior.Contains("raidPilotProjectedDisplayYaw(", StringComparison.Ordinal)
            && behavior.Contains("pose.x,", StringComparison.Ordinal)
            && behavior.Contains("pose.z,", StringComparison.Ordinal),
            "la flèche locale n'est pas projetée géométriquement par Leaflet");
    }

    private static void KeepsPlayerCenteredWhileMapMoves()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        Require(
            behavior.Contains("mapInstance.setView(playerLatLng, currentZoom, { animate: false })", StringComparison.Ordinal)
            && behavior.Contains("container.clientWidth) * 0.5", StringComparison.Ordinal)
            && behavior.Contains("container.clientHeight) * 0.5", StringComparison.Ordinal)
            && behavior.Contains("GPS-style center lock", StringComparison.Ordinal),
            "le joueur n'est pas verrouillé au centre de la carte");
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
