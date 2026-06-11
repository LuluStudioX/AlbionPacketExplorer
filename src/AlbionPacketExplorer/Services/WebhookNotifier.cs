using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Posts protocol-change notifications to a user-supplied webhook. Discord webhook URLs get a
/// Discord-shaped <c>{ content }</c> payload; anything else receives a structured JSON object. The
/// URL is the only thing sent off the machine, and only when the user has opted in and a change is
/// found.
/// </summary>
public static class WebhookNotifier
{
    public sealed record SendResult(bool Ok, string Message);

    private const string UserName = "APX Protocol Scanner";
    private const int MaxListed = 25;

    public static Task<SendResult> SendTestAsync(string url, CancellationToken ct = default)
    {
        var payload = IsDiscord(url)
            ? DiscordBody($"**{UserName}** test - if you can read this, change notifications are wired up correctly.")
            : JsonSerializer.Serialize(new { source = "AlbionPacketExplorer", @event = "test" });
        return PostAsync(url, payload, ct);
    }

    public static Task<SendResult> SendChangeAsync(string url, ProtocolScanResult r, CancellationToken ct = default)
    {
        var payload = IsDiscord(url) ? DiscordBody(BuildMarkdown(r)) : BuildJson(r);
        return PostAsync(url, payload, ct);
    }

    private static bool IsDiscord(string url) =>
        url.Contains("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("discordapp.com/api/webhooks", StringComparison.OrdinalIgnoreCase);

    private static string DiscordBody(string content)
    {
        if (content.Length > 1900) content = content[..1900] + "\n...";
        return JsonSerializer.Serialize(new { username = UserName, content });
    }

    private static string BuildMarkdown(ProtocolScanResult r)
    {
        var sb = new StringBuilder();
        sb.Append("**Albion protocol change detected**\n");
        sb.Append($"Client `{r.ClientVersion ?? "unknown"}` - ");
        sb.Append($"new: {r.AddedCount}, moved: {r.ShiftedCount}, removed: {r.RemovedCount}\n");

        int shown = 0;
        foreach (var c in r.Changes)
        {
            if (shown++ >= MaxListed) { sb.Append($"...and {r.Changes.Count - MaxListed} more\n"); break; }
            sb.Append(c.Type switch
            {
                ProtocolChangeType.Added   => $"+ new `{c.Enum}.{c.Name}` = {c.ClientCode}\n",
                ProtocolChangeType.Removed => $"- removed `{c.Enum}.{c.Name}` (was {c.AppCode})\n",
                // Migration: the event kept its identity but its wire code moved.
                _                          => $"~ moved `{c.Enum}.{c.Name}`: code {c.AppCode} -> {c.ClientCode}\n",
            });
        }
        return sb.ToString();
    }

    private static string BuildJson(ProtocolScanResult r) =>
        JsonSerializer.Serialize(new
        {
            source = "AlbionPacketExplorer",
            @event = "protocol-change",
            clientVersion = r.ClientVersion,
            summary = $"new: {r.AddedCount}, shifted: {r.ShiftedCount}, removed: {r.RemovedCount}",
            added = r.Changes.Where(c => c.Type == ProtocolChangeType.Added)
                .Select(c => new { c.Enum, c.Name, code = c.ClientCode }),
            shifted = r.Changes.Where(c => c.Type == ProtocolChangeType.Shifted)
                .Select(c => new { c.Enum, c.Name, appCode = c.AppCode, clientCode = c.ClientCode }),
            removed = r.Changes.Where(c => c.Type == ProtocolChangeType.Removed)
                .Select(c => new { c.Enum, c.Name, appCode = c.AppCode }),
        });

    private static async Task<SendResult> PostAsync(string url, string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new SendResult(false, "Invalid webhook URL.");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(uri, content, ct);
            if (response.IsSuccessStatusCode) return new SendResult(true, "");
            var body = await response.Content.ReadAsStringAsync(ct);
            return new SendResult(false, $"{(int) response.StatusCode} {response.ReasonPhrase}: {Trim(body)}");
        }
        catch (TaskCanceledException) { return new SendResult(false, "timeout"); }
        catch (HttpRequestException ex) { return new SendResult(false, ex.Message); }
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200];
}
