namespace RaidPilot.Overlay.Tests;

internal static class OverlayLayoutCalculatorTests
{
    internal const int TestCount = 16;
    internal static void RunAll(ICollection<string> failures)
    {
        Run("coin supérieur droit à 96 DPI", () => BoundsAt(96, new(1072,16,832,412)), failures);
        Run("coin supérieur droit à 144 DPI", () => BoundsAt(144, new(648,24,1248,618)), failures);
        Run("les quatre coins sont disponibles", SupportsAllCorners, failures);
        Run("position libre reste proportionnelle à l'écran", FreeLayoutRoundTrips, failures);
        Run("infos suivent l'agrandissement de la carte", ContentScaleFollowsWindowSize, failures);
        Run("surface de carte intacte à chaque DPI", PreservesMapViewport, failures);
        Run("ordre CSS et JavaScript extraction-carte-quêtes", SectionOrder, failures);
        Run("mise à jour préserve les captures existantes", UpdateDoesNotPurgeScreenshots, failures);
        Run("moniteur décalé et barre des tâches respectés", OtherMonitor, failures);
        Run("quêtes déployées vers le bas sans saut de carte", QuestExtendsDown, failures);
        Run("DPI invalide utilise 96", InvalidDpi, failures);
        Run("échelle manuelle réduit toute la fenêtre", ManualScaleShrinksWindow, failures);
        Run("échelle manuelle ne dépasse jamais l'écran", ManualScaleNeverLeavesWorkingArea, failures);
        Run("mode auto réduit les petits écrans", AutomaticScaleAdaptsToSmallScreen, failures);
        Run("mode auto garde 100% sur grand écran", AutomaticScaleKeepsLargeScreenAtFullSize, failures);
        Run("flashs quêtes et extractions restent ralentis", AttentionFlashesAreCalm, failures);
    }
    private static int Scale(int v,int dpi) => (int)Math.Round(v*dpi/96.0,MidpointRounding.AwayFromZero);
    private static void BoundsAt(int dpi, Rectangle expected)
    {
        var bounds=OverlayLayoutCalculator.CalculateBounds(new(0,0,1920,1080),dpi);
        Require(bounds==expected,$"Bornes incorrectes : {bounds}");
        Require(bounds.Top==Scale(16,dpi),"Marge haute incorrecte");
        Require(bounds.Right==1920-Scale(16,dpi),"Marge droite incorrecte");
    }
    private static void SupportsAllCorners()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        Require(
            OverlayLayoutCalculator.CalculateBounds(
                area, 96, false, 1.0, OverlayCorner.TopLeft)
                == new Rectangle(16, 16, 832, 412),
            "haut gauche incorrect");
        Require(
            OverlayLayoutCalculator.CalculateBounds(
                area, 96, false, 1.0, OverlayCorner.TopRight)
                == new Rectangle(1072, 16, 832, 412),
            "haut droite incorrect");
        Require(
            OverlayLayoutCalculator.CalculateBounds(
                area, 96, false, 1.0, OverlayCorner.BottomLeft)
                == new Rectangle(16, 652, 832, 412),
            "bas gauche incorrect");
        Require(
            OverlayLayoutCalculator.CalculateBounds(
                area, 96, false, 1.0, OverlayCorner.BottomRight)
                == new Rectangle(1072, 652, 832, 412),
            "bas droite incorrect");
    }

    private static void FreeLayoutRoundTrips()
    {
        var area = new Rectangle(-1920, 48, 1920, 1032);
        var requested = new Rectangle(-1560, 190, 620, 430);
        var layout = OverlayLayoutCalculator.NormalizeFreeLayout(requested, area);
        var restored = OverlayLayoutCalculator.CalculateFreeBounds(area, 96, layout);

        Require(Math.Abs(restored.Left - requested.Left) <= 1, "x libre perdu");
        Require(Math.Abs(restored.Top - requested.Top) <= 1, "y libre perdu");
        Require(Math.Abs(restored.Width - requested.Width) <= 1, "largeur libre perdue");
        Require(Math.Abs(restored.Height - requested.Height) <= 1, "hauteur libre perdue");
        Require(area.Contains(restored), "la position libre sort de l'écran");
    }

    private static void ContentScaleFollowsWindowSize()
    {
        var reference = new Rectangle(0, 0, 832, 412);
        Require(
            Math.Abs(OverlayLayoutCalculator.CalculateContentScale(
                new Rectangle(0, 0, 1664, 824),
                reference) - 2.0) < 0.0001,
            "une carte doublée devrait doubler textes et informations");
        Require(
            Math.Abs(OverlayLayoutCalculator.CalculateContentScale(
                new Rectangle(0, 0, 2496, 1236),
                reference) - 3.0) < 0.0001,
            "l'ancienne limite 150% ne doit plus bloquer l'agrandissement");
        Require(
            Math.Abs(OverlayLayoutCalculator.CalculateContentScale(
                new Rectangle(0, 0, 1664, 618),
                reference) - 1.5) < 0.0001,
            "l'axe limitant doit empêcher les informations de sortir verticalement");
    }

    private static void PreservesMapViewport()
    {
        foreach(var dpi in new[]{96,120,144,192})
        foreach(var shown in new[]{false,true})
        {
            var area=new Rectangle(0,0,3440,1392);
            var bounds=OverlayLayoutCalculator.CalculateBounds(area,dpi,shown);
            var quest=shown?Scale(96,dpi):0;
            Require(bounds.Height-Scale(72,dpi)-quest==Scale(340,dpi),"Carte réduite");
            Require(bounds.Width==Scale(832,dpi),"Largeur changée");
            Require(bounds.Top==area.Top+Scale(16,dpi),"Ensemble non ancré en haut");
        }
    }
    private static void SectionOrder()
    {
        var css=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedStyles.css")).Replace("\r\n","\n");
        var js=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedBehavior.js"));
        Require(css.Contains("--raidpilot-guidance-height: 72px"),"Hauteur extraction absente");
        Require(css.Contains("inset: var(--raidpilot-guidance-height) 0 var(--raidpilot-quest-height) 0"),"Carte pas entre les panneaux");
        Require(css.Contains("#raidpilot-guidance {\n    position: fixed !important;\n    top: 0 !important;\n    bottom: auto !important;"),"Extraction pas en haut");
        Require(css.Contains("top: auto !important; bottom: 0 !important; left: 0 !important; right: 0 !important;"),"Quêtes pas en bas");
        Require(css.Contains("top: calc(var(--raidpilot-guidance-height) + 8px)"),"Compteur de raid pas dans le coin de carte");
        Require(js.Contains("inset: \"var(--raidpilot-guidance-height, 72px) 0px var(--raidpilot-quest-height, 0px) 0px\""),"JavaScript inverse la disposition CSS");
        Require(js.Contains("height: mapViewportHeight")&&js.Contains("observeMapViewportSize"),"Taille Leaflet non synchronisée");
    }
    private static void OtherMonitor()
    {
        var area=new Rectangle(-1920,48,1920,1032);
        var b=OverlayLayoutCalculator.CalculateBounds(area,96,true);
        Require(b==new Rectangle(-848,64,832,508),$"Moniteur décalé incorrect : {b}");
    }
    private static void QuestExtendsDown()
    {
        foreach(var dpi in new[]{96,120,144,192})
        {
            var area=new Rectangle(3440,-1440,2560,1392);
            var normal=OverlayLayoutCalculator.CalculateBounds(area,dpi);
            var expanded=OverlayLayoutCalculator.CalculateBounds(area,dpi,true);
            Require(normal.Location==expanded.Location,"L’apparition de la quête déplace la carte");
            Require(normal.Width==expanded.Width,"La quête change la largeur");
            Require(expanded.Height-normal.Height==Scale(96,dpi),"Pied de quête de mauvaise hauteur");
            Require(expanded.Bottom-normal.Bottom==Scale(96,dpi),"La quête ne s’ouvre pas vers le bas");
        }
    }
    private static void ManualScaleShrinksWindow()
    {
        var area=new Rectangle(0,0,1920,1080);
        var full=OverlayLayoutCalculator.CalculateBounds(area,96,false,1.0);
        var reduced=OverlayLayoutCalculator.CalculateBounds(area,96,false,0.70);
        Require(reduced.Width==(int)Math.Round(full.Width*0.70,MidpointRounding.AwayFromZero),"Largeur 70% incorrecte");
        Require(reduced.Height==(int)Math.Round(full.Height*0.70,MidpointRounding.AwayFromZero),"Hauteur 70% incorrecte");
        Require(reduced.Right==full.Right,"Ancrage droit perdu");
        Require(reduced.Top==full.Top,"Ancrage haut perdu");
    }

    private static void ManualScaleNeverLeavesWorkingArea()
    {
        var area=new Rectangle(100,40,1100,680);
        var bounds=OverlayLayoutCalculator.CalculateBounds(area,144,true,1.20);
        Require(bounds.Left>=area.Left,"La carte dépasse à gauche");
        Require(bounds.Top>=area.Top,"La carte dépasse en haut");
        Require(bounds.Right<=area.Right,"La carte dépasse à droite");
        Require(bounds.Bottom<=area.Bottom,"La carte dépasse en bas");
    }

    private static void AutomaticScaleAdaptsToSmallScreen()
    {
        var area=new Rectangle(0,0,1366,728);
        var scale=OverlayLayoutCalculator.CalculateAutomaticScale(area,96,true);
        Require(scale<1.0,"Le mode Auto n'a pas réduit un écran 1366x728");
        Require(scale>=0.50,"Le mode Auto réduit trop agressivement");
        var bounds=OverlayLayoutCalculator.CalculateBounds(area,96,true,scale);
        Require(bounds.Width<=area.Width*0.60,"La carte Auto reste trop large");
        Require(bounds.Bottom<=area.Bottom,"La carte Auto dépasse verticalement");
    }

    private static void AutomaticScaleKeepsLargeScreenAtFullSize()
    {
        var area=new Rectangle(0,0,2560,1400);
        var scale=OverlayLayoutCalculator.CalculateAutomaticScale(area,96,true);
        Require(Math.Abs(scale-1.0)<0.0001,$"Grand écran réduit à {scale:P0}");
    }

    private static void AttentionFlashesAreCalm()
    {
        var css=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedStyles.css"));
        Require(css.Contains("raidpilot-quest-pulse 1.8s steps(2, end)",StringComparison.Ordinal),"Flash quête trop rapide");
        Require(css.Contains("raidpilot-extraction-pulse 1.8s steps(2, end)",StringComparison.Ordinal),"Flash extraction trop rapide");
        Require(css.Contains("raidpilot-target-pulse 1.4s steps(2, end)",StringComparison.Ordinal),"Flash cible trop rapide");
        Require(css.Contains("raidpilot-extraction-ring 1.8s ease-out",StringComparison.Ordinal),"Cercle extraction trop rapide");
        Require(css.Contains("animation-duration: 1.4s !important",StringComparison.Ordinal),"Cercle cible trop rapide");
    }

    private static void InvalidDpi()
    {
        var area=new Rectangle(0,0,1920,1080);
        Require(OverlayLayoutCalculator.CalculateBounds(area,0)==OverlayLayoutCalculator.CalculateBounds(area,96),"DPI nul incorrect");
        Require(OverlayLayoutCalculator.CalculateBounds(area,-1,true)==OverlayLayoutCalculator.CalculateBounds(area,96,true),"DPI négatif incorrect");
    }
    private static void UpdateDoesNotPurgeScreenshots()
    {
        Require(ScreenshotFolderWatcher.IsStartupCleanupSuppressed("1"),"Préservation inactive");
        Require(!ScreenshotFolderWatcher.IsStartupCleanupSuppressed(null),"Valeur par défaut modifiée");
        Require(!ScreenshotFolderWatcher.IsStartupCleanupSuppressed("0"),"Valeur inactive ignorée");
    }
    private static void Run(string name, Action action, ICollection<string> failures)
    {
        try{action();Console.WriteLine("OK  "+name);}
        catch(Exception error){failures.Add(name+" — "+error.Message);}
    }
    private static void Require(bool condition,string message)
    { if(!condition)throw new InvalidOperationException(message); }
}