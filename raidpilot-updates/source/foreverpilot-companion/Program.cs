using System.Diagnostics;
using System.Drawing;

namespace ForeverPilot.Companion;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--self-test")){SelfTests.Run();return;}
        var verifyIndex=Array.IndexOf(args,"--verify-access");
        if(verifyIndex>=0 && verifyIndex+1<args.Length){
            using var input=System.Text.Json.JsonDocument.Parse(File.ReadAllText(args[verifyIndex+1]));
            var client=new AccessClient(args.Contains("--sandbox"));
            var verified=client.Refresh(input.RootElement.GetProperty("key").GetString()!).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"activation-verification.json"),System.Text.Json.JsonSerializer.Serialize(new {verified=true,environment=verified.Record.Environment,expires=verified.Record.Expires,paidUntil=verified.Record.PaidUntil,checkedAt=DateTimeOffset.UtcNow}));return;
        }
        ApplicationConfiguration.Initialize();Application.Run(new AccessForm(args.Contains("--sandbox")));
    }
}
internal sealed class AccessForm : Form
{
    readonly AccessClient client;Settings settings;readonly bool sandbox;
    readonly Label title=new(),intro=new(),keyLabel=new(),pathLabel=new(),status=new(),hint=new();
    readonly TextBox key=new(){UseSystemPasswordChar=true},path=new(){ReadOnly=true};
    readonly Button portal=new(),choose=new(),activate=new(),import=new();readonly ComboBox language=new();
    readonly System.Windows.Forms.Timer timer=new(){Interval=3600000};bool working;
    string T(string fr,string en)=>settings.Language=="en"?en:fr;
    internal AccessForm(bool test)
    {
        sandbox=test;client=new(test);settings=client.Load();Text="ForeverPilot";ClientSize=new Size(540,510);MinimumSize=Size;MaximumSize=Size;
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(12,24,32);ForeColor=Color.FromArgb(235,243,245);Font=new Font("Segoe UI",10);
        title.SetBounds(28,24,330,40);title.Font=new Font("Segoe UI",23,FontStyle.Bold);
        language.SetBounds(389,31,122,30);language.DropDownStyle=ComboBoxStyle.DropDownList;language.Items.AddRange(["Français","English"]);language.SelectedIndex=settings.Language=="en"?1:0;
        intro.SetBounds(30,76,480,48);intro.ForeColor=Color.Silver;
        portal.SetBounds(30,135,480,42);keyLabel.SetBounds(30,196,480,24);key.SetBounds(30,224,350,30);import.SetBounds(392,223,118,32);
        pathLabel.SetBounds(30,273,480,24);path.SetBounds(30,302,350,30);choose.SetBounds(392,301,118,32);
        activate.SetBounds(30,355,480,43);activate.BackColor=Color.FromArgb(101,227,202);activate.ForeColor=Color.FromArgb(14,35,36);activate.FlatStyle=FlatStyle.Flat;
        status.SetBounds(30,413,480,48);status.ForeColor=Color.FromArgb(101,227,202);hint.SetBounds(30,470,480,28);hint.Font=new Font("Segoe UI",8);hint.ForeColor=Color.Silver;
        Controls.AddRange([title,language,intro,portal,keyLabel,key,import,pathLabel,path,choose,activate,status,hint]);
        key.Text=settings.Key;path.Text=settings.GamePath;
        if(string.IsNullOrEmpty(path.Text)){var candidate=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),"World of Warcraft","_forever_");if(File.Exists(Path.Combine(candidate,"Wow.exe")))path.Text=candidate;}
        language.SelectedIndexChanged+=(_,_)=>{settings=settings with {Language=language.SelectedIndex==1?"en":"fr"};client.Save(settings);Labels();};
        portal.Click+=(_,_)=>Process.Start(new ProcessStartInfo(client.Origin){UseShellExecute=true});
        import.Click+=(_,_)=>{
            using var dialog=new OpenFileDialog {Filter="ForeverPilot key (*.json)|*.json",CheckFileExists=true};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try {if(new FileInfo(dialog.FileName).Length>4096)throw new InvalidDataException();using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(dialog.FileName));
                if(json.RootElement.GetProperty("service").GetString()!=client.Origin)throw new InvalidDataException();
                var value=json.RootElement.GetProperty("key").GetString()!;if(!System.Text.RegularExpressions.Regex.IsMatch(value,"^[A-Za-z0-9_-]{43}$"))throw new InvalidDataException();key.Text=value;
                status.Text=T("Clé importée. Clique Activer pour la vérifier.","Key imported. Click Activate to verify it.");}
            catch {status.Text=T("Ce fichier n’est pas une clé pour cet environnement ForeverPilot.","This file is not a key for this ForeverPilot environment.");}
        };
        choose.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog{Description=T("Choisis le dossier _forever_ de WoW.","Choose the WoW _forever_ folder."),UseDescriptionForTitle=true,ShowNewFolderButton=false};if(dialog.ShowDialog(this)==DialogResult.OK)path.Text=dialog.SelectedPath;};
        activate.Click+=async(_,_)=>await UpdateAccess(false);timer.Tick+=async(_,_)=>await UpdateAccess(true);timer.Start();
        Shown+=async(_,_)=>{if(settings.Key.Length>0)await UpdateAccess(true);};FormClosed+=(_,_)=>timer.Dispose();Labels();
    }
    void Labels()
    {
        title.Text="ForeverPilot";intro.Text=sandbox?T("Mode de test PayPal — aucun paiement réel.\nUne activation de test ne modifie pas le jeu.","PayPal test mode — no real payments.\nTest access never modifies the game."):T("Ton abonnement, connecté à WoW Forever.\nL’édition RaidPilot reste gratuite et n’a pas besoin de cet outil.","Your subscription, connected to WoW Forever.\nThe free RaidPilot edition does not need this tool.");
        portal.Text=T("1. Ouvrir mon abonnement","1. Open my subscription");keyLabel.Text=T("2. Colle la clé personnelle du portail","2. Paste your personal key from the portal");pathLabel.Text=T("3. Dossier WoW Forever","3. WoW Forever folder");choose.Text=T("Parcourir…","Browse…");
        activate.Text=sandbox?T("Vérifier l’activation de test","Verify test activation"):T("Activer / actualiser mon accès","Activate / refresh my access");
        import.Text=T("Importer…","Import…");
        hint.Text=T("Clé conservée chiffrée pour ton compte Windows uniquement.","Your key is encrypted for your Windows account only.");
    }
    async Task UpdateAccess(bool background)
    {
        if(working)return;working=true;activate.Enabled=false;
        try {
            if(!background)status.Text=T("Vérification sécurisée en cours…","Secure verification in progress…");
            var gamePath=sandbox?path.Text:AccessClient.ValidateGamePath(path.Text);
            var result=await client.Refresh(key.Text.Trim());
            if(!sandbox)AccessClient.Install(gamePath,result.Record,false);
            settings=settings with {Key=key.Text.Trim(),GamePath=gamePath,Token=result.Token};client.Save(settings);
            var until=DateTimeOffset.FromUnixTimeSeconds(result.Record.PaidUntil).LocalDateTime.ToShortDateString();
            status.Text=sandbox?T("Test réussi : paiement confirmé et activation vérifiée.","Test passed: payment confirmed and activation verified."):T("Accès actif jusqu’au ","Access active until ")+until+T(".\nReconnecte-toi à WoW pour charger l’activation.",".\nLog back into WoW to load the activation.");
        } catch(Exception e) {
            if(e.Message=="NO_ACTIVE_ACCESS"&&!sandbox){
                try { AccessClient.Revoke(path.Text);settings=settings with {Token=""};client.Save(settings); }
                catch { status.Text=T("Accès inactif. Ferme WoW, puis réessaie pour actualiser les fichiers.","Access inactive. Close WoW, then retry to refresh the files.");return; }
            }
            status.Text=e.Message switch {
                "INVALID_KEY"=>T("Clé non reconnue. Copie-la depuis ton portail ForeverPilot.","Key not recognized. Copy it from your ForeverPilot portal."),
                "NO_ACTIVE_ACCESS"=>T("Aucun paiement confirmé. Vérifie ton abonnement dans le portail.","No confirmed payment. Check your subscription in the portal."),
                "CLOSE_WOW_FIRST"=>T("Ferme WoW pour installer l’activation, puis réessaie.","Close WoW to install the activation, then try again."),
                "GAME_NOT_FOUND"=>T("Choisis le dossier _forever_ contenant Wow.exe.","Choose the _forever_ folder containing Wow.exe."),
                _=>T("Vérification impossible pour le moment. Réessaie. L’activation existante n’a pas été remplacée.","Verification unavailable. Try again. Your existing activation was not replaced.")};
        } finally {working=false;activate.Enabled=true;}
    }
}
