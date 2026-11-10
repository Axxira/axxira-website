using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DungeonMapPilot.Companion;
internal sealed record AccessRecord(string Subject, long Expires, long PaidUntil, string Environment);
internal sealed record Settings(string Key="", string GamePath="", string Language="fr", string Token="");
internal sealed class AccessClient(bool sandbox)
{
    internal string EnvironmentName => sandbox ? "sandbox" : "live";
    internal string Origin => sandbox ? "https://dungeonmappilot-subscriptions-sandbox.tyronixstudio.workers.dev" : "https://dungeonmappilot-subscriptions.tyronixstudio.workers.dev";
    private string StorePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"DungeonMapPilot",sandbox?"access-sandbox.bin":"access.bin");
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4));
    internal static AccessRecord Verify(string token, string publicKey, string origin, string environment, long now)
    {
        if(token.Length>16384)throw new InvalidDataException("INVALID_LICENSE");
        var parts=token.Split('.');if(parts.Length!=3)throw new InvalidDataException("INVALID_LICENSE");
        using var header=JsonDocument.Parse(Decode(parts[0]));using var jwk=JsonDocument.Parse(publicKey);
        if(header.RootElement.GetProperty("alg").GetString()!="RS256" || header.RootElement.GetProperty("kid").GetString()!=jwk.RootElement.GetProperty("kid").GetString())throw new InvalidDataException("INVALID_LICENSE");
        using var rsa=RSA.Create();rsa.ImportParameters(new RSAParameters {Modulus=Decode(jwk.RootElement.GetProperty("n").GetString()!),Exponent=Decode(jwk.RootElement.GetProperty("e").GetString()!)});
        if(!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("INVALID_SIGNATURE");
        using var claims=JsonDocument.Parse(Decode(parts[1]));var c=claims.RootElement;
        var exp=c.GetProperty("exp").GetInt64();var issued=c.GetProperty("iat").GetInt64();var paid=c.GetProperty("paidUntil").GetInt64();
        var subject=c.GetProperty("sub").GetString()!;
        if(c.GetProperty("iss").GetString()!=origin||c.GetProperty("aud").GetString()!="dungeonmappilot"||c.GetProperty("environment").GetString()!=environment||!Guid.TryParse(subject,out _))throw new InvalidDataException("WRONG_LICENSE");
        if(exp<=now||issued>now+300||exp<=issued||exp>issued+7*86400||paid<exp)throw new InvalidDataException("EXPIRED_LICENSE");
        var features=c.GetProperty("features").EnumerateArray().Select(x=>x.GetString()).ToArray();
        if(c.GetProperty("product").GetString()!="dungeon_maps"||features.Length!=1||!features.Contains("dungeon_maps"))throw new InvalidDataException("INCOMPLETE_LICENSE");
        return new(subject,exp,paid,environment);
    }
    internal Settings Load()
    {
        try {return JsonSerializer.Deserialize<Settings>(ProtectedData.Unprotect(File.ReadAllBytes(StorePath),null,DataProtectionScope.CurrentUser))??new();}
        catch {return new();}
    }
    internal void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings),null,DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StorePath+".tmp",bytes);File.Move(StorePath+".tmp",StorePath,true);
    }
    internal async Task<(AccessRecord Record,string Token)> Refresh(string key)
    {
        if(!Regex.IsMatch(key,"^[A-Za-z0-9_-]{43}$"))throw new InvalidDataException("INVALID_KEY");
        using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(40)};
        using var request=new HttpRequestMessage(HttpMethod.Post,Origin+"/api/license");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Content=new StringContent("{}",Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode){var code=response.StatusCode switch {System.Net.HttpStatusCode.Unauthorized=>"INVALID_KEY",System.Net.HttpStatusCode.Forbidden=>"NO_ACTIVE_ACCESS",_=>"SERVICE_UNAVAILABLE"};throw new InvalidOperationException(code);}
        var content=await response.Content.ReadAsStringAsync();if(content.Length>32768)throw new InvalidDataException("INVALID_LICENSE");
        using var json=JsonDocument.Parse(content);var token=json.RootElement.GetProperty("token").GetString()!;
        var publicKey=await http.GetStringAsync(Origin+"/api/public-key");
        return (Verify(token,publicKey,Origin,EnvironmentName,DateTimeOffset.UtcNow.ToUnixTimeSeconds()),token);
    }
    internal static string ValidateGamePath(string path)
    {
        var root=Path.GetFullPath(path);
        if(!string.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)),"_forever_",StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(root,"Wow.exe")))throw new InvalidDataException("GAME_NOT_FOUND");
        return root;
    }
    internal static void Install(string path, AccessRecord record, bool sandbox)
    {
        var root=ValidateGamePath(path);
        if(sandbox)throw new InvalidOperationException("SANDBOX_NO_GAME_INSTALL"); // Test payments can never unlock the real addon.
        if(record.Environment!="live"||record.Expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidDataException("EXPIRED_LICENSE");
        var folder=Path.Combine(root,"Interface","AddOns","DungeonMapPilot_Access");
        if(!Directory.Exists(folder)&&System.Diagnostics.Process.GetProcessesByName("Wow").Length>0)throw new InvalidOperationException("CLOSE_WOW_FIRST");
        Directory.CreateDirectory(folder);
        const string toc="## Interface: 16001\n## Title: DungeonMapPilot Access\n## Notes: Access refreshed by the DungeonMapPilot companion.\n## Version: 1.0.0\nAccess.lua\n";
        var body=$"-- Refreshed by DungeonMapPilot Companion. No PayPal credentials are stored here.\nDungeonMapPilotAccess = {{ environment=\"live\", expires={record.Expires}, paidUntil={record.PaidUntil}, subject=\"{record.Subject}\", product=\"dungeon_maps\", dungeonMaps=true }}\n";
        // Only this dedicated access addon is touched. Neither leveling, Ding nor the legacy bridge is replaced.
        if(!File.Exists(Path.Combine(folder,"DungeonMapPilot_Access.toc")))File.WriteAllText(Path.Combine(folder,"DungeonMapPilot_Access.toc"),toc,new UTF8Encoding(false));
        WriteAccess(folder,body);
    }
    private static void WriteAccess(string folder,string body)
    {
        var destination=Path.Combine(folder,"Access.lua");
        if(File.Exists(destination)){
            if(File.ReadAllText(destination)==body)return;
            var backup=Path.Combine(folder,"Access.previous.lua.bak");
            File.Copy(destination,backup,true);
        }
        File.WriteAllText(destination+".tmp",body,new UTF8Encoding(false));File.Move(destination+".tmp",destination,true);
    }
    internal static void Revoke(string path)
    {
        var folder=Path.Combine(ValidateGamePath(path),"Interface","AddOns","DungeonMapPilot_Access");
        var destination=Path.Combine(folder,"Access.lua");
        if(File.Exists(destination)&&File.ReadAllText(destination).StartsWith("-- Refreshed by DungeonMapPilot Companion."))
            WriteAccess(folder,"-- Refreshed by DungeonMapPilot Companion. Access no longer active.\nDungeonMapPilotAccess = nil\n");
    }
}
