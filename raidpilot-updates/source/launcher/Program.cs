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
        if (args.Length == 2 && args[0] == "--render-habs-preview")
        {
            using var preview = HabsGoalArt.Render(1920, 1.1, "APERÇU  ·  CANADIENS DE MONTRÉAL");
            preview.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        Application.Run(new LauncherForm());
    }
}
