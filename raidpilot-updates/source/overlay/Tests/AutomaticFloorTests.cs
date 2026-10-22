using System.Diagnostics;
using System.Text.Json;
namespace RaidPilot.Overlay.Tests;
internal static class AutomaticFloorTests
{
    internal const int TestCount=13;
    private static ScreenshotObservation Shot(double y=4)=>new("test-fixture",0,y,0,0,0,0,1,0);
    internal static void RunAll(ICollection<string> failures)
    {
        Run("étages enregistre l'altitude native",()=>{var e=new AutomaticFloorEvidence();Require(e.Record(Shot(),"customs",1,100,"native"));Require(e.Anchor!.Y==4);},failures);
        Run("étages conserve la source visuelle explicite",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(),"factory",1,100,"visual");Require(e.Anchor!.Source=="visual");},failures);
        Run("étages ignore la hauteur non finie",()=>{var e=new AutomaticFloorEvidence();Require(!e.Record(Shot(double.NaN),"customs",1,100,"native"));},failures);
        Run("étages ignore une source estimative non mesurée",()=>{var e=new AutomaticFloorEvidence();Require(!e.Record(Shot(),"customs",1,100,"dead-reckoning"));},failures);
        Run("étages ne renouvelle pas une ancienne observation",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(),"customs",1,100,"native");var id=e.Anchor!.Id;
            var text=JsonSerializer.Serialize(e.GetAnchorPayload("customs",1,100+5*Stopwatch.Frequency));using var json=JsonDocument.Parse(text);Require(json.RootElement.GetProperty("age").GetDouble()==5 && e.Anchor.Id==id);},failures);
        Run("étages refuse observation d'un ancien raid",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(),"customs",1,100,"native");Require(e.GetAnchorPayload("customs",2,200) is null);},failures);
        Run("étages refuse observation d'une autre carte",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(),"customs",1,100,"native");Require(e.GetAnchorPayload("factory",1,200) is null);},failures);
        Run("étages reset retire l'ancienne hauteur",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(),"customs",1,100,"native");e.Reset();Require(e.Anchor is null);},failures);
        Run("étages refuse une capture arrivée en désordre",()=>{var e=new AutomaticFloorEvidence();e.Record(Shot(8),"customs",1,200,"native");Require(!e.Record(Shot(4),"customs",1,100,"native") && e.Anchor!.Y==8);},failures);
        Run("lanceur mise à jour préserve les captures",()=>Require(Program.PreserveScreenshotsRequested(new[]{"app.exe","--preserve-screenshots"})),failures);
        Run("option de préservation absente",()=>Require(!Program.PreserveScreenshotsRequested(new[]{"app.exe"})),failures);
        Run("option de préservation exige une correspondance exacte",()=>Require(!Program.PreserveScreenshotsRequested(new[]{"app.exe","--preserve-screenshots=no"})),failures);
        Run("étages ressources autonomes sans nouveau déclenchement de capture",()=>{
            var module=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"AutomaticFloors.js"));
            var js=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedBehavior.js"));
            Require(js.Contains("__raidPilotSetFloorPose")&&js.Contains("__raidPilotClearFloors")&&module.Contains("class Tracker")
                &&!module.Contains("new WebSocket")&&!module.Contains("fetch(")&&!module.Contains("SendInput"));},failures);
    }
    private static void Require(bool condition){if(!condition)throw new InvalidOperationException("Assertion étage échouée");}
    private static void Run(string name,Action action,ICollection<string> failures){try{action();Console.WriteLine("OK  "+name);}catch(Exception e){failures.Add(name+" — "+e.Message);}}
}
