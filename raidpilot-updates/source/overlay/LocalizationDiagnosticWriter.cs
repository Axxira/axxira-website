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

    internal static void RecordAnchor(RealtimePose predicted, ScreenshotObservation exact, string map)
    {
        try
        {
            if (!File.Exists(EnablePath)) return;
            var values = new[] {predicted.X,predicted.Z,predicted.YawDegrees,
                predicted.PositionUncertaintyMeters,exact.X,exact.Z,exact.YawDegrees};
            if (values.Any(v => !double.IsFinite(v))) return;
            var dx = predicted.X - exact.X;
            var dz = predicted.Z - exact.Z;
            var distance = Math.Sqrt(dx * dx + dz * dz);
            var yaw = (predicted.YawDegrees - exact.YawDegrees + 540) % 360 - 180;
            string f(double value) => value.ToString("0.###",CultureInfo.InvariantCulture);
            var safeMap = new string((map ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(48).ToArray());
            var line = string.Join(",",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),
                safeMap,f(predicted.X),f(predicted.Z),f(predicted.YawDegrees),
                f(exact.X),f(exact.Z),f(exact.YawDegrees),f(distance),f(yaw),
                f(predicted.PositionUncertaintyMeters));
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(CsvPath) && new FileInfo(CsvPath).Length >= MaximumBytes)
                    File.Move(CsvPath,CsvPath + ".previous",true);
                var fresh = !File.Exists(CsvPath) || new FileInfo(CsvPath).Length == 0;
                using var writer = new StreamWriter(CsvPath, append: true, encoding: new UTF8Encoding(false));
                if (fresh) writer.WriteLine("utc,map,pred_x,pred_z,pred_yaw,exact_x,exact_z,exact_yaw,error_m,yaw_error_deg,uncertainty_m");
                writer.WriteLine(line);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
