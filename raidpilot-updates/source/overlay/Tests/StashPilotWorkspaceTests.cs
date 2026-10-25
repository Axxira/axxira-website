namespace RaidPilot.Overlay.Tests;

internal static class StashPilotWorkspaceTests
{
    internal const int TestCount = 4;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("progression sauvegardée et rechargée sans modification", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "stashpilot-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "progress.json");
            try
            {
                Require(StashPilotWorkspace.TrySave(
                    "Boulons;4;FIR;CONFIRMÉ", "Boulons;3;Planque;;CONFIRMÉ", path));
                Require(StashPilotWorkspace.TryLoad(out var inventory, out var requirements, path)
                    && inventory.Contains("Boulons;4")
                    && requirements.Contains("Planque"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }, failures);

        Run("état absent n'implique aucune progression", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            Require(!StashPilotWorkspace.TryLoad(out _, out _, path));
        }, failures);

        Run("progression corrompue refusée sans planter", () =>
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{invalid}");
                Require(!StashPilotWorkspace.TryLoad(out _, out _, path));
            }
            finally { File.Delete(path); }
        }, failures);

        Run("progression trop volumineuse refusée", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Require(!StashPilotWorkspace.TrySave(new string('X', 256001), "", path));
            Require(!File.Exists(path));
        }, failures);
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("OK  " + name); }
        catch (Exception error) { failures.Add(name + " : " + error.Message); }
    }

    private static void Require(bool ok)
    {
        if (!ok) throw new InvalidOperationException("StashPilot workspace");
    }
}
