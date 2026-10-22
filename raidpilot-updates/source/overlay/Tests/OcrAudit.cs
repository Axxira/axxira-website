using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class OcrAudit
{
    internal static async Task<int> Run(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Dossier de captures introuvable.");
            return 2;
        }

        var files = Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reader = new WindowsScreenshotTextReader();
        var rows = new List<object>();
        var panelCandidates = 0;
        var timerSuccesses = 0;
        var failures = 0;

        foreach (var file in files)
        {
            try
            {
                var lines = await reader.ReadExtractionPanelLinesAsync(file, CancellationToken.None);
                var exfilLines = ExtractionAdvisor.SelectUniqueOcrExfilLines(lines);
                var plan = RaidPanelReadPlan.FromLines(lines, false);
                if (!plan.ReadTimer)
                {
                    continue;
                }

                panelCandidates++;
                var panelTimerOk = RaidTimerParser.TryParseRemainingSeconds(lines, out var panelSeconds);
                var timerLines = await reader.ReadRaidTimerLinesAsync(file, CancellationToken.None);
                var timerOk = RaidTimerParser.TryParseRemainingSeconds(timerLines, out var seconds);
                if (timerOk) timerSuccesses++;

                rows.Add(new
                {
                    file = Path.GetFileName(file),
                    exfilSlots = exfilLines.Count,
                    panelTimerOk,
                    panelRemainingSeconds = panelTimerOk ? panelSeconds : (int?)null,
                    timerOk,
                    remainingSeconds = timerOk ? seconds : (int?)null,
                    timerLines = timerOk ? Array.Empty<string>() : timerLines.ToArray(),
                });
            }
            catch (Exception exception)
            {
                failures++;
                rows.Add(new
                {
                    file = Path.GetFileName(file),
                    error = exception.GetType().Name,
                });
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scanned = files.Length,
            panelCandidates,
            timerSuccesses,
            failures,
            rows,
        }, new JsonSerializerOptions { WriteIndented = true }));

        return failures == 0 ? 0 : 1;
    }
}
