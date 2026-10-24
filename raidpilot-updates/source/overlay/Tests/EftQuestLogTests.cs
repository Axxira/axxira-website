using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class EftQuestLogTests
{
    internal const int TestCount = 14;
    private const string TaskId = "0123456789abcdef01234567";
    private static string Event(int type, long dt=1792828800) =>
        JsonSerializer.Serialize(new
        {
            type = "new_message",
            message = new { type, templateId = TaskId + " successMessageText", dt }
        }, new JsonSerializerOptions { WriteIndented = true });
    private static string AsLog(int type, long dt=1792828800) =>
        "2026-10-24 08:12:03.000|Info|push-notifications|Got notification | ChatMessageReceived\n"
        + Event(type,dt) + "\n";

    internal static void RunAll(ICollection<string> failures)
    {
        Run("Quête journal : tâche acceptée type 10",()=>{
            Require(EftQuestLogParser.TryParseNotification(Event(10),out var item)
                && item?.TaskId==TaskId && item.Status=="active"
                && item.AtUnixSeconds==1792828800);
        }, failures);
        Run("Quête journal : échec type 11",()=>{
            Require(EftQuestLogParser.TryParseNotification(Event(11),out var item)
                && item?.Status=="failed");
        }, failures);
        Run("Quête journal : remise type 12",()=>{
            Require(EftQuestLogParser.TryParseNotification(Event(12),out var item)
                && item?.Status=="completed");
        }, failures);
        Run("Quête journal : message marché ignoré",()=>{
            Require(!EftQuestLogParser.TryParseNotification(Event(4),out _));
        }, failures);
        Run("Quête journal : pas de progression sans new_message",()=>{
            var payload = "{\"type\":\"notification\",\"message\":{\"type\":12,\"templateId\":\""
                +TaskId+"\",\"dt\":1792828800}}";
            Require(!EftQuestLogParser.TryParseNotification(payload,out _));
        }, failures);
        Run("Quête journal : ID public 24 hex imposé",()=>{
            Require(!EftQuestLogParser.TryParseNotification(Event(12).Replace(TaskId,"../../secret"),out _));
        }, failures);
        Run("Quête journal : horodatage valide exigé",()=>{
            Require(!EftQuestLogParser.TryParseNotification(Event(12,0),out _));
        }, failures);
        Run("Quête journal : payload trop long refusé",()=>{
            Require(!EftQuestLogParser.TryParseNotification(new string('x',66000),out _));
        }, failures);
        Run("Journal EFT : mode PvE et PvP explicitement reconnus",()=>{
            Require(EftQuestLogParser.DetectSessionMode("Game: Session mode: Pve")=="pve");
            Require(EftQuestLogParser.DetectSessionMode("Game: Session mode: Pvp")=="regular");
            Require(EftQuestLogParser.DetectSessionMode("Game: Session mode: PvpSeason")=="pvp-season");
            Require(EftQuestLogParser.DetectSessionMode("without any mode")==null);
            Require(EftQuestLogParser.DetectSessionMode("Session mode: unknown")==null);
        }, failures);
        Run("Journal EFT : ancienne quête ignorée au démarrage",()=>{
            WithLog((dir,log)=>{
                File.WriteAllText(log,AsLog(12));
                var found=new List<EftQuestLogEvent>();
                using var watcher=new EftQuestLogWatcher(dir,TimeSpan.FromDays(1));
                watcher.QuestChanged += found.Add;
                Require(watcher.Start());
                watcher.Poll();
                Require(found.Count==0);
            });
        }, failures);
        Run("Journal EFT : nouvelle notification multi-ligne détectée une fois",()=>{
            WithLog((dir,log)=>{
                File.WriteAllText(log,"existing log\n");
                var found=new List<EftQuestLogEvent>();
                using var watcher=new EftQuestLogWatcher(dir,TimeSpan.FromDays(1));
                watcher.QuestChanged += found.Add;
                Require(watcher.Start());
                var payload=AsLog(10);
                File.AppendAllText(log,payload);
                watcher.Poll();
                Require(found.Count==1&&found[0].Status=="active");
                watcher.Poll();
                Require(found.Count==1);
                File.AppendAllText(log,payload);
                watcher.Poll();
                Require(found.Count==1);
            });
        }, failures);
        Run("Journal EFT : une ligne JSON incomplète attend la suite",()=>{
            WithLog((dir,log)=>{
                File.WriteAllText(log,"init\n");
                var found=new List<EftQuestLogEvent>();
                using var watcher=new EftQuestLogWatcher(dir,TimeSpan.FromDays(1));
                watcher.QuestChanged += found.Add;
                Require(watcher.Start());
                var serialized=AsLog(12);
                File.AppendAllText(log,serialized[..^2]);
                watcher.Poll();
                Require(found.Count==0);
                File.AppendAllText(log,serialized[^2..]);
                watcher.Poll();
                Require(found.Count==1&&found[0].Status=="completed");
            });
        }, failures);
        Run("Journal EFT : nouvelle session et rotation traitées",()=>{
            WithLog((dir,log)=>{
                File.WriteAllText(log,"init\n");
                using var watcher=new EftQuestLogWatcher(dir,TimeSpan.FromDays(1));
                var found=new List<EftQuestLogEvent>();
                watcher.QuestChanged += found.Add;
                Require(watcher.Start());
                var next=Path.Combine(dir,"log_20261025");
                Directory.CreateDirectory(next);
                var rotated=Path.Combine(next,"push-notifications_000.log");
                File.WriteAllText(rotated,AsLog(11,1792915200));
                File.SetLastWriteTimeUtc(log,DateTime.UtcNow.AddHours(-3));
                File.SetLastWriteTimeUtc(rotated,DateTime.UtcNow);
                watcher.Poll();
                Require(found.Count==1&&found[0].Status=="failed");
            });
        }, failures);
        Run("Journal EFT : mode transmis après transition de carte",()=>{
            var root=Path.Combine(Path.GetTempPath(),"raidpilot-quest-mode-"+Guid.NewGuid().ToString("N"));
            var folder=Path.Combine(root,"Logs","log_20261024");
            Directory.CreateDirectory(folder);
            var app=Path.Combine(folder,"application_000.log");
            try{
                File.WriteAllText(app,
                    "2026-10-24 08:00:00.000|application|TRACE-NetworkGameCreate Location: Customs\n"
                    +"2026-10-24 08:00:01.000|application|Session mode: Pve\n");
                var order=new List<string>();
                using var watcher=new EftMapLogWatcher(Path.Combine(root,"Logs"),
                    TimeSpan.FromDays(1),watchFileSystemEvents:false);
                watcher.MapDetected+=(_,a)=>order.Add("map:"+a.NormalizedName);
                watcher.GameModeDetected+=(_,a)=>order.Add("mode:"+a.GameMode);
                Require(watcher.Start());
                Require(order.SequenceEqual(["map:customs","mode:pve"]));
            }finally{Directory.Delete(root,true);}
        }, failures);
    }

    private static void WithLog(Action<string,string> test)
    {
        var dir=Path.Combine(Path.GetTempPath(),"raidpilot-questlog-"+Guid.NewGuid().ToString("N"));
        var session=Path.Combine(dir,"log_20261024");
        Directory.CreateDirectory(session);
        var log=Path.Combine(session,"push-notifications_000.log");
        try{test(dir,log);}finally{Directory.Delete(dir,true);}
    }
    private static void Run(string name,Action test,ICollection<string> failures)
    {
        try{test();Console.WriteLine("OK  "+name);}
        catch(Exception ex){failures.Add(name+" : "+ex.Message);}
    }
    private static void Require(bool ok)
    {
        if(!ok)throw new InvalidOperationException("Assertion de journal EFT échouée");
    }
}
