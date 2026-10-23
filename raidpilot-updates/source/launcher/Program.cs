namespace RaidPilot.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // A packaged-runtime smoke test: no window, network or player data.
        if (args.Contains("--verify-bundle", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = LauncherPackageVerifier.Verify(
                AppContext.BaseDirectory, out _) ? 0 : 3;
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}
