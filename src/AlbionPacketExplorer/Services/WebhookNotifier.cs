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
        var line = $"**{UserName}** test - if you can read this, change notifications are wired up correctly.";
        var payload = IsDiscord(url)
            ? DiscordBody($"{line}\n\nExamples:\n{BuildMarkdown(SampleResult())}")
            : JsonSerializer.Serialize(new
            {
                source = "AlbionPacketExplorer",
                @event = "test",
                example = BuildMarkdown(SampleResult()),
            });
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

        // Grouped by type with a header each (only when non-empty). Emoji on dark themes stay visible.
        var budget = MaxListed;
        AppendGroup(sb, "New \U0001F195", r.Changes, ProtocolChangeType.Added,
            c => $"`{c.Enum}.{c.Name}` = {c.ClientCode}", ref budget);
        AppendGroup(sb, "Moved \U0001F500", r.Changes, ProtocolChangeType.Shifted,
            c => $"`{c.Enum}.{c.Name}`: code {c.AppCode} -> {c.ClientCode}", ref budget);
        AppendGroup(sb, "Removed ❌", r.Changes, ProtocolChangeType.Removed,
            c => $"`{c.Enum}.{c.Name}` (was {c.AppCode})", ref budget);
        return sb.ToString();
    }

    // One section per change type, listed only when it has entries, sharing a global line budget.
    private static void AppendGroup(StringBuilder sb, string title, IReadOnlyList<ProtocolChange> changes,
        ProtocolChangeType type, Func<ProtocolChange, string> format, ref int budget)
    {
        var items = changes.Where(c => c.Type == type).ToList();
        if (items.Count == 0) return;
        sb.Append($"\n**{title}**\n");
        foreach (var c in items)
        {
            if (budget-- <= 0) { sb.Append("...and more\n"); return; }
            sb.Append("• ").Append(format(c)).Append('\n');
        }
    }

    // A representative change set so the test message can show what a real alert looks like.
    private static ProtocolScanResult SampleResult() => new(
        true, null, "1.31.021.334290", null, "example", false,
        [
            new("EventCodes", ProtocolChangeType.Added, "NotifyPlatformAccountConfirmed", null, 683),
            new("EventCodes", ProtocolChangeType.Shifted, "NewSimpleItem", 32, 33),
            new("EventCodes", ProtocolChangeType.Shifted, "NewBuilding", 45, 46),
            new("EventCodes", ProtocolChangeType.Removed, "SomePrototypeEvent", 410, null),
        ]);

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
