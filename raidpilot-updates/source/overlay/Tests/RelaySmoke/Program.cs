using RaidPilot.Overlay;
using System.Security.Cryptography;

// Live smoke with randomly generated squad and synthetic coordinates.
// Never reads the user's game, profile, screenshots or actual position.
var alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
var chars = new char[16];
for (var i = 0; i < chars.Length; i++)
    chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
var code = SquadRoomCode.Format(new string(chars));
var identityA = "test-a-" + Guid.NewGuid().ToString("N");
var identityB = "test-b-" + Guid.NewGuid().ToString("N");
await using var a = new SquadRelayClient(code, identityA, "RelayTestA");
await using var b = new SquadRelayClient(code, identityB, "RelayTestB");
var gotA = new TaskCompletionSource<SquadMemberPose>(TaskCreationOptions.RunContinuationsAsynchronously);
var gotB = new TaskCompletionSource<SquadMemberPose>(TaskCreationOptions.RunContinuationsAsynchronously);
a.PoseReceived += pose => { if (pose.Name == "RelayTestB") gotA.TrySetResult(pose); };
b.PoseReceived += pose => { if (pose.Name == "RelayTestA") gotB.TrySetResult(pose); };
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
try
{
    await a.StartReceivingAsync(timeout.Token);
    await b.StartReceivingAsync(timeout.Token);
    // The two independent receiver loops must both be listening before a send.
    await Task.Delay(350, timeout.Token);
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    await a.SendPoseAsync(new SquadMemberPose(
        identityA, "RelayTestA", "customs", 100.5, 0, -25, 90, 2, now), timeout.Token);
    var fromA = await gotB.Task.WaitAsync(TimeSpan.FromSeconds(12), timeout.Token);
    if (fromA.Map != "customs" || Math.Abs(fromA.X - 100.5) > 0.001)
        throw new InvalidOperationException("Receiver B did not get A's coordinates");
    await b.SendPoseAsync(new SquadMemberPose(
        identityB, "RelayTestB", "customs", 102.0, 1, -27, 270, 3,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), timeout.Token);
    var fromB = await gotA.Task.WaitAsync(TimeSpan.FromSeconds(12), timeout.Token);
    if (Math.Abs(fromB.X - 102.0) > 0.001 || Math.Abs(fromB.YawDegrees - 270) > 0.001)
        throw new InvalidOperationException("Receiver A did not get B's coordinates/heading");
    Console.WriteLine("PASS: RaidPilot C# client sends and receives actual WebSocket poses in both directions via Cloudflare.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: Cloudflare C# relay integration: " + error.GetType().Name + " " + error.Message);
    return 1;
}
