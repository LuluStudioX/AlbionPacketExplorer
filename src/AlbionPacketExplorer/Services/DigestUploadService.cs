using System.Net.Http;
using System.Text;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Sends a schema digest to the project's collection endpoint (a Cloudflare Worker that
/// validates the shape and stores it for schema folding). The endpoint holds no secrets and
/// accepts nothing but digest-shaped JSON; the client carries no credentials at all.
/// </summary>
public static class DigestUploadService
{
    public const string EndpointUrl = "https://apx-digest.workpressfail2ban.workers.dev/v1/digest";

    public sealed record UploadResult(bool Ok, string Message);

    public static async Task<UploadResult> UploadAsync(string digestJson, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new StringContent(digestJson, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(EndpointUrl, content, ct);

            if (response.IsSuccessStatusCode)
                return new UploadResult(true, "");

            var body = await response.Content.ReadAsStringAsync(ct);
            return new UploadResult(false, $"{(int) response.StatusCode} {response.ReasonPhrase}: {Trim(body)}");
        }
        catch (TaskCanceledException)
        {
            return new UploadResult(false, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new UploadResult(false, ex.Message);
        }
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200];
}
