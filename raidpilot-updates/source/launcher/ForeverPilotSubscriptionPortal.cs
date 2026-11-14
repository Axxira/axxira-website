using System.Text.Json;

namespace RaidPilot.Launcher;

internal static class ForeverPilotSubscriptionPortal
{
    internal const string Url = "https://foreverpilot-subscriptions.tyronixstudio.workers.dev/#account-title";
    internal const string ConfigUrl = "https://foreverpilot-subscriptions.tyronixstudio.workers.dev/api/config";
    internal static bool IsAllowed(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "foreverpilot-subscriptions.tyronixstudio.workers.dev"
        && uri.AbsolutePath == "/" && uri.Fragment == "#account-title"
        && string.IsNullOrEmpty(uri.Query);

    internal static bool IsExpectedConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement;
            return data.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True
                && data.TryGetProperty("environment", out var env) && env.GetString() == "live"
                && data.TryGetProperty("price", out var price) && price.GetString() == "6.99"
                && data.TryGetProperty("currency", out var currency) && currency.GetString() == "CAD"
                && data.TryGetProperty("interval", out var interval) && interval.GetString() == "MONTH";
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static async Task<bool> IsLivePriceReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        try
        {
            using var response = await client.GetAsync(ConfigUrl);
            if (!response.IsSuccessStatusCode) return false;
            return IsExpectedConfig(await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }
}
