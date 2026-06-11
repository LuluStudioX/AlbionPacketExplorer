using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Submits a detected protocol change (new/shifted enum codes) to the project's collection endpoint
/// so a maintainer can fold it into the shipped enums. Anonymous: only the client version and the
/// changed code names/numbers are sent, and only when the user explicitly approves the upload. The
/// client carries no credentials.
/// </summary>
public static class ProtocolUploadService
{
    public const string EndpointUrl = "https://apx-digest.workpressfail2ban.workers.dev/v1/protocol";

    public sealed record UploadResult(bool Ok, string Message);

    /// <summary>Exactly what would leave the machine, so it can be previewed before approval.</summary>
    public static string BuildPayload(ProtocolScanResult r, string appVersion) =>
        JsonSerializer.Serialize(new
        {
            v = 1,
            app = appVersion,
            clientVersion = r.ClientVersion,
            createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            changes = r.Changes.Select(c => new
            {
                @enum = c.Enum,
                type = c.Type.ToString().ToLowerInvariant(),
                name = c.Name,
                appCode = c.AppCode,
                clientCode = c.ClientCode,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });

    public static async Task<UploadResult> UploadAsync(string json, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(EndpointUrl, content, ct);
            if (response.IsSuccessStatusCode) return new UploadResult(true, "");
            var body = await response.Content.ReadAsStringAsync(ct);
            return new UploadResult(false, $"{(int) response.StatusCode} {response.ReasonPhrase}: {Trim(body)}");
        }
        catch (TaskCanceledException) { return new UploadResult(false, "timeout"); }
        catch (HttpRequestException ex) { return new UploadResult(false, ex.Message); }
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200];
}
