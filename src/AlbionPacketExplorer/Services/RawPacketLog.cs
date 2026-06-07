namespace AlbionPacketExplorer.Services;

/// <summary>
/// Dev aid: when APX_SAVE_RAW is set, append every captured raw packet (base64, one per line) so the
/// decoder rewrite can be parity-verified offline (replay raw -> old vs new decoder, diff output).
/// Off by default; raw bytes are never written otherwise.
/// </summary>
public static class RawPacketLog
{
    private static readonly bool Enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APX_SAVE_RAW"));

    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void MaybeSave(byte[] payload)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                _writer ??= Open();
                _writer.WriteLine(Convert.ToBase64String(payload));
            }
        }
        catch { /* best effort */ }
    }

    private static StreamWriter Open()
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        FilePath = Path.Combine(AppPaths.LogsDir, $"raw-packets_{DateTime.Now:yyyyMMdd_HHmmss}.b64");
        return new StreamWriter(FilePath, append: true) { AutoFlush = true };
    }
}
