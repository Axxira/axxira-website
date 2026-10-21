namespace RaidPilot.Overlay.Tests;

internal static class MapQualityTests
{
    internal const int TestCount = 10;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("map quality: rendu vectoriel haute précision", UsesHighPrecisionVectorRendering, failures);
        Run("map quality: halo d'incertitude métrique", UsesMetricUncertaintyHalos, failures);
        Run("map quality: traces tactiques bornées", UsesBoundedTacticalTrails, failures);
        Run("map quality: traces reprojetées par Leaflet", TrailsFollowLiveLeafletProjection, failures);
        Run("map quality: rayon de cap local et squad", UsesHeadingRays, failures);
        Run("map quality: couche tactique sous les pions", TacticalLayerStaysBelowMarkers, failures);
        Run("map quality: empreinte de formation", UsesSquadFormationFootprint, failures);
        Run("map quality: décluttering adaptatif", UsesAdaptiveLabelDecluttering, failures);
        Run("map quality: teammates hors écran", UsesOffscreenSquadIndicators, failures);
        Run("map quality: étage actif accentué", EmphasizesConfirmedFloor, failures);
    }

    private static (string Behavior, string Styles) Load()
    {
        return (
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css")));
    }

    private static void UsesHighPrecisionVectorRendering()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("settings.style = \"svg\"", StringComparison.Ordinal)
            && styles.Contains("shape-rendering: geometricPrecision", StringComparison.Ordinal)
            && styles.Contains("text-rendering: geometricPrecision", StringComparison.Ordinal)
            && !styles.Contains("filter: blur(", StringComparison.OrdinalIgnoreCase),
            "le rendu haute précision SVG n'est pas verrouillé");
    }

    private static void UsesMetricUncertaintyHalos()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("raidPilotMetricRadiusPixels", StringComparison.Ordinal)
            && behavior.Contains("latLngToContainerPoint([z, x + meters])", StringComparison.Ordinal)
            && behavior.Contains("--raidpilot-local-uncertainty-radius", StringComparison.Ordinal)
            && behavior.Contains("--raidpilot-squad-uncertainty-radius", StringComparison.Ordinal)
            && styles.Contains("#leaflet-map > #raidpilot-local-player-marker::before", StringComparison.Ordinal)
            && styles.Contains("#leaflet-map > .raidpilot-squad-marker::before", StringComparison.Ordinal),
            "le halo d'incertitude n'est pas dérivé des mètres de la carte");
    }

    private static void UsesBoundedTacticalTrails()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("tacticalTrailLifetimeMs = 7000", StringComparison.Ordinal)
            && behavior.Contains("tacticalTrailPointLimit = 48", StringComparison.Ordinal)
            && behavior.Contains("meters >= 12 && elapsed < 1200", StringComparison.Ordinal)
            && behavior.Contains("raidpilot-tactical-trails", StringComparison.Ordinal)
            && styles.Contains("raidpilot-tactical-trail-local-recent", StringComparison.Ordinal)
            && styles.Contains("raidpilot-tactical-trail-squad-recent", StringComparison.Ordinal),
            "les traces tactiques ne sont pas bornées ou anti-téléportation");
    }

    private static void TrailsFollowLiveLeafletProjection()
    {
        var (behavior, _) = Load();
        Require(
            behavior.Contains("latLngToContainerPoint([sample.z, sample.x])", StringComparison.Ordinal)
            && behavior.Contains("tacticalTrailEvents = [\"move\", \"zoom\", \"resize\", \"viewreset\"]", StringComparison.Ordinal)
            && behavior.Contains("ResizeObserver(schedule)", StringComparison.Ordinal),
            "les traces ne suivent pas les transformations de la carte");
    }

    private static void UsesHeadingRays()
    {
        var (_, styles) = Load();
        Require(
            styles.Contains(".raidpilot-local-player-heading::before", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-heading::before", StringComparison.Ordinal)
            && styles.Contains("linear-gradient(to top", StringComparison.Ordinal),
            "le rayon de cap lisible n'est pas présent sur tous les pions");
    }

    private static void TacticalLayerStaysBelowMarkers()
    {
        var (_, styles) = Load();
        Require(
            styles.Contains("#leaflet-map > #raidpilot-tactical-trails", StringComparison.Ordinal)
            && styles.Contains("z-index: 1080", StringComparison.Ordinal)
            && styles.Contains("z-index: 1150", StringComparison.Ordinal)
            && styles.Contains("z-index: 1200", StringComparison.Ordinal),
            "l'ordre des couches tactiques n'est pas verrouillé");
    }

    private static void UsesSquadFormationFootprint()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("convexHull", StringComparison.Ordinal)
            && behavior.Contains("tacticalFormationPath", StringComparison.Ordinal)
            && behavior.Contains("formationPoints", StringComparison.Ordinal)
            && behavior.Contains("raidpilot-squad-formation", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-formation", StringComparison.Ordinal),
            "l'empreinte de formation de l'escouade est absente");
    }

    private static void UsesAdaptiveLabelDecluttering()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("updateAdaptiveMapLabels", StringComparison.Ordinal)
            && behavior.Contains("pixelsPerMeter < 0.9", StringComparison.Ordinal)
            && behavior.Contains("maximumHidden", StringComparison.Ordinal)
            && behavior.Contains("raidpilot-map-label-decluttered", StringComparison.Ordinal)
            && styles.Contains(".map-area-label.raidpilot-map-label-decluttered", StringComparison.Ordinal),
            "le décluttering adaptatif des noms de lieux est absent");
    }

    private static void UsesOffscreenSquadIndicators()
    {
        var (behavior, styles) = Load();
        Require(
            behavior.Contains("makeSquadEdgeIndicator", StringComparison.Ordinal)
            && behavior.Contains("updateSquadEdgeIndicator", StringComparison.Ordinal)
            && behavior.Contains("raidpilot-squad-edge-indicator", StringComparison.Ordinal)
            && behavior.Contains("Math.atan2(dy, dx)", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-edge-indicator", StringComparison.Ordinal)
            && styles.Contains(".raidpilot-squad-edge-arrow", StringComparison.Ordinal),
            "les indicateurs de teammates hors écran sont absents");
    }

    private static void EmphasizesConfirmedFloor()
    {
        var (_, styles) = Load();
        Require(
            styles.Contains("#raidpilot-auto-floor[data-state=\"measured\"]", StringComparison.Ordinal)
            && styles.Contains("#raidpilot-auto-floor[data-state=\"recognized\"]", StringComparison.Ordinal)
            && styles.Contains("html.raidpilot-auto-floor-filter #leaflet-map", StringComparison.Ordinal)
            && styles.Contains("opacity: 0.28", StringComparison.Ordinal),
            "l'étage actif confirmé n'est pas visuellement accentué");
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine("OK  " + name);
        }
        catch (Exception exception)
        {
            failures.Add(name + ": " + exception.Message);
            Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
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