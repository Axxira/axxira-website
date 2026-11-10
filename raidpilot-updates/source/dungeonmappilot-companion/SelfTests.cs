using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DungeonMapPilot.Companion;
internal static class SelfTests
{
    internal static void Run()
    {
        using var rsa=RSA.Create(2048);var p=rsa.ExportParameters(false);const string origin="https://example.test";const long now=1800000000;
        static string Enc(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        var publicKey=JsonSerializer.Serialize(new {kty="RSA",kid="test",n=Enc(p.Modulus!),e=Enc(p.Exponent!)});
        string Make(string environment="sandbox",long expires=now+3600,string issuer=origin,long issued=now,string audience="dungeonmappilot",string product="dungeon_maps",string[]? features=null){var header=Enc(JsonSerializer.SerializeToUtf8Bytes(new{alg="RS256",kid="test"}));var body=Enc(JsonSerializer.SerializeToUtf8Bytes(new{iss=issuer,aud=audience,sub="032f2954-0068-4bd5-ae51-309d1ca9394a",iat=issued,exp=expires,paidUntil=now+864000,environment,product,features=features??new[]{"dungeon_maps"}}));var data=header+"."+body;return data+"."+Enc(rsa.SignData(Encoding.ASCII.GetBytes(data),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));}
        var valid=Make();AccessClient.Verify(valid,publicKey,origin,"sandbox",now);int rejected=0;
        foreach(var value in new[]{Make(environment:"live"),Make(expires:now-1),Make(issuer:"https://evil.test"),Make(issued:now+600),Make(expires:now+8*86400),Make(audience:"foreverpilot"),Make(product:"leveling"),Make(features:["fast_xp","class_guidance"]),Make(features:["dungeon_maps","fast_xp"]),valid[..^12]+"AAAAAAAAAAAA"}){
            try{AccessClient.Verify(value,publicKey,origin,"sandbox",now);throw new Exception("UNSAFE_LICENSE_ACCEPTED");}catch(InvalidDataException){rejected++;}catch(CryptographicException){rejected++;}}
        if(rejected!=10)throw new Exception("TEST_FAILURE");
        // All writes stay in a fresh disposable fixture, never the actual game.
        var fixture=Path.Combine(AppContext.BaseDirectory,"test-fixtures",Guid.NewGuid().ToString(),"_forever_");
        Directory.CreateDirectory(fixture);File.WriteAllText(Path.Combine(fixture,"Wow.exe"),"fixture");
        var addons=Path.Combine(fixture,"Interface","AddOns");var maps=Path.Combine(addons,"DungeonMapPilot_Access");Directory.CreateDirectory(maps);
        foreach(var name in new[]{"ForeverPilot_Access","ForeverPilot","ForeverPilot_Leveling","Ding"}){var folder=Path.Combine(addons,name);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"preserved.txt"),name);}
        var current=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var record=new AccessRecord("032f2954-0068-4bd5-ae51-309d1ca9394a",current+3600,current+86400,"live");
        AccessClient.Install(fixture,record,false);
        var lua=File.ReadAllText(Path.Combine(maps,"Access.lua"));
        if(!lua.Contains("DungeonMapPilotAccess")||!lua.Contains("product=\"dungeon_maps\"")||lua.Contains("fastXP"))throw new Exception("WRONG_ACCESS_FILE");
        try{AccessClient.Install(fixture,record,true);throw new Exception("SANDBOX_INSTALLED");}catch(InvalidOperationException e)when(e.Message=="SANDBOX_NO_GAME_INSTALL"){}
        AccessClient.Revoke(fixture);
        if(!File.ReadAllText(Path.Combine(maps,"Access.lua")).Contains("DungeonMapPilotAccess = nil"))throw new Exception("REVOKE_FAILED");
        foreach(var name in new[]{"ForeverPilot_Access","ForeverPilot","ForeverPilot_Leveling","Ding"})if(File.ReadAllText(Path.Combine(addons,name,"preserved.txt"))!=name)throw new Exception("OTHER_ADDON_MODIFIED");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-result.txt"),"PASS: RSA, issuer, audience, product, exact features, environment, expiry, bounded validity, tampering, Maps install/revoke, Sandbox rejection and preservation of leveling/bridge/Ding.\n");
    }
}
