using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ForeverPilot.Companion;
internal static class SelfTests
{
    internal static void Run()
    {
        using var rsa=RSA.Create(2048);var p=rsa.ExportParameters(false);const string origin="https://example.test";const long now=1800000000;
        static string Enc(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
        var publicKey=JsonSerializer.Serialize(new {kty="RSA",kid="test",n=Enc(p.Modulus!),e=Enc(p.Exponent!)});
        string Make(string environment="sandbox",long expires=now+3600,string issuer=origin,long issued=now){var header=Enc(JsonSerializer.SerializeToUtf8Bytes(new{alg="RS256",kid="test"}));var body=Enc(JsonSerializer.SerializeToUtf8Bytes(new{iss=issuer,aud="foreverpilot",sub="032f2954-0068-4bd5-ae51-309d1ca9394a",iat=issued,exp=expires,paidUntil=now+864000,environment,features=new[]{"fast_xp","class_guidance"}}));var data=header+"."+body;return data+"."+Enc(rsa.SignData(Encoding.ASCII.GetBytes(data),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));}
        var valid=Make();AccessClient.Verify(valid,publicKey,origin,"sandbox",now);int rejected=0;
        foreach(var value in new[]{Make(environment:"live"),Make(expires:now-1),Make(issuer:"https://evil.test"),Make(issued:now+600),Make(expires:now+8*86400),valid[..^12]+"AAAAAAAAAAAA"}){
            try{AccessClient.Verify(value,publicKey,origin,"sandbox",now);throw new Exception("UNSAFE_LICENSE_ACCEPTED");}catch(InvalidDataException){rejected++;}catch(CryptographicException){rejected++;}}
        if(rejected!=6)throw new Exception("TEST_FAILURE");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-result.txt"),"PASS: RSA signature, issuer, audience, environment, expiry, issue time, bounded validity and tampering checks.\n");
    }
}
