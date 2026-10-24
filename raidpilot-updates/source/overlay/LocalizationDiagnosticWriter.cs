using System.Globalization;
using System.Text;

namespace RaidPilot.Overlay;

/// <summary>
/// Diagnostic opt-in local pour comparer l'estimation à chaque position EFT exacte.
/// Aucun PNG, identifiant de joueur ou touche clavier n'est journalisé.
/// Activer avec la présence d'un fichier vide "diagnostic-localisation.on"
/// dans le dossier LocalAppData/RaidPilot. Supprimer ce fichier pour arrêter.
/// </summary>
internal static class LocalizationDiagnosticWriter
{
    private static readonly object Gate = new();
    private const long MaximumBytes = 2 * 1024 * 1024;
    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RaidPilot");
    internal static string EnablePath => Path.Combine(DirectoryPath, "diagnostic-localisation.on");
    internal static string CsvPath => Path.Combine(DirectoryPath, "diagnostic-localisation.csv");
    // Optional: a user-selected local folder already synchronized by Google Drive Desktop.
    internal static string SyncFolderConfigPath => Path.Combine(DirectoryPath, "diagnostic-sync-folder.txt");

    private static void SyncToUserSelectedFolder()
    {
        try
        {
            if (!File.Exists(SyncFolderConfigPath)) return;
            var location = File.ReadAllText(SyncFolderConfigPath).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location)
                || location.StartsWith(new string(Path.DirectorySeparatorChar, 2), StringComparison.Ordinal)
                || !Directory.Exists(location)) return;
            var destination = Path.GetFullPath(location);
            if (destination.TrimEnd(Path.DirectorySeparatorChar).Equals(
                Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) return;
            var target = Path.Combine(destination, "raidpilot-localization-diagnostic.csv");
            var tmp = target + ".tmp";
            File.Copy(CsvPath, tmp, true);
            File.Move(tmp, target, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
    }


    internal static void RecordAnchor(RealtimePose? predicted, ScreenshotObservation exact, string map)
    {
        try
        {
            if (!File.Exists(EnablePath)) return;
            var valid = predicted is { } pose && new[] {pose.X,pose.Z,pose.YawDegrees,
                pose.PositionUncertaintyMeters,exact.X,exact.Z,exact.YawDegrees}.All(double.IsFinite);
            if (!double.IsFinite(exact.X) || !double.IsFinite(exact.Z)
                || !double.IsFinite(exact.YawDegrees)) return;
            string f(double value) => value.ToString("0.###",CultureInfo.InvariantCulture);
            var safeMap = new string((map ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(48).ToArray());
            var fields = valid
                ? new[] { f(pose.X),f(pose.Z),f(pose.YawDegrees),f(exact.X),f(exact.Z),
                    f(exact.YawDegrees),f(Math.Hypot(pose.X-exact.X,pose.Z-exact.Z)),
                    f((pose.YawDegrees-exact.YawDegrees+540)%360-180),
                    f(pose.PositionUncertaintyMeters),"compared" }
                : new[] { "","","",f(exact.X),f(exact.Z),f(exact.YawDegrees),
                    "","","","no_historical_pose" };
            var line = DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)
                + "," + safeMap + "," + string.Join(",",fields);
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(CsvPath) && new FileInfo(CsvPath).Length >= MaximumBytes)
                    File.Move(CsvPath,CsvPath + ".previous",true);
                var fresh = !File.Exists(CsvPath) || new FileInfo(CsvPath).Length == 0;
                using var writer = new StreamWriter(CsvPath, append: true, encoding: new UTF8Encoding(false));
                if (fresh) writer.WriteLine("utc,map,pred_x,pred_z,pred_yaw,exact_x,exact_z,exact_yaw,error_m,yaw_error_deg,uncertainty_m,status");
                writer.WriteLine(line);
                writer.Flush();
                SyncToUserSelectedFolder();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
