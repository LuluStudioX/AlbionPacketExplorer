using System.Text.Json;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

public enum ProtocolChangeType { Added, Removed, Shifted }

/// <summary>A single difference between the live client enum and the app's compiled enum.</summary>
/// <param name="Enum">"EventCodes" or "OperationCodes".</param>
/// <param name="AppCode">The code the app currently uses (null when the member is new to the client).</param>
/// <param name="ClientCode">The code the live client uses (null when the member was removed).</param>
public sealed record ProtocolChange(
    string Enum, ProtocolChangeType Type, string Name, int? AppCode, int? ClientCode);

public sealed record ProtocolScanResult(
    bool Ok,
    string? Error,
    string? ClientVersion,
    string? MetadataPath,
    string Fingerprint,
    bool LowConfidence,
    IReadOnlyList<ProtocolChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
    public int AddedCount => Changes.Count(c => c.Type == ProtocolChangeType.Added);
    public int RemovedCount => Changes.Count(c => c.Type == ProtocolChangeType.Removed);
    public int ShiftedCount => Changes.Count(c => c.Type == ProtocolChangeType.Shifted);

    public static ProtocolScanResult Fail(string error) =>
        new(false, error, null, null, "", false, []);
}

/// <summary>Persisted watermark so a webhook fires once per client patch, not on every scan.</summary>
public sealed record ProtocolScanState(string? LastNotifiedFingerprint = null, string? LastClientVersion = null)
{
    public static ProtocolScanState Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ProtocolState)) return new ProtocolScanState();
            return JsonSerializer.Deserialize<ProtocolScanState>(File.ReadAllText(AppPaths.ProtocolState))
                   ?? new ProtocolScanState();
        }
        catch { return new ProtocolScanState(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ProtocolState)!);
            File.WriteAllText(AppPaths.ProtocolState, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

/// <summary>
/// Compares the live Albion client's protocol enums (read from its <c>global-metadata.dat</c>)
/// against the app's compiled <see cref="EventCodes"/> / <see cref="OperationCodes"/> and reports
/// what changed: new codes, removed codes, and shifted ordinals. Pure read-only inspection of a
/// local file the user already has; nothing is sent anywhere by this service.
/// </summary>
public sealed class ProtocolScanService
{
    // Stable members that have never moved; if any of these mismatch, the parse/layout is wrong
    // (a real game patch only ever shifts higher codes), so we flag the result as low-confidence.
    private static readonly (string Enum, string Name, int Code)[] Anchors =
    [
        ("EventCodes", "Leave", 1),
        ("EventCodes", "Move", 3),
        ("EventCodes", "NewSimpleItem", 32),
        ("EventCodes", "NewBuilding", 45),
        ("OperationCodes", "Move", 22),
    ];

    public ProtocolScanResult Scan(string? clientPathOverride)
    {
        var client = AlbionClientLocator.Locate(clientPathOverride);
        if (client is null)
            return ProtocolScanResult.Fail("Albion client not found. Set the path in settings.");

        var dumps = Il2CppMetadataReader.ReadEnums(client.MetadataPath,
        [
            ("Albion.Common.Photon", "EventCodes"),
            ("Albion.Common.Photon", "OperationCodes"),
        ]);
        if (dumps is null || !dumps.ContainsKey("EventCodes") || !dumps.ContainsKey("OperationCodes"))
            return ProtocolScanResult.Fail("Could not read protocol enums from the client metadata.");

        var live = new Dictionary<string, Dictionary<string, int>>
        {
            ["EventCodes"] = dumps["EventCodes"].Members.ToDictionary(m => m.Name, m => m.Value),
            ["OperationCodes"] = dumps["OperationCodes"].Members.ToDictionary(m => m.Name, m => m.Value),
        };

        bool lowConfidence = Anchors.Any(a =>
            !live[a.Enum].TryGetValue(a.Name, out var v) || v != a.Code);

        var app = new Dictionary<string, Dictionary<string, int>>
        {
            ["EventCodes"] = EnumMap<EventCodes>(),
            ["OperationCodes"] = EnumMap<OperationCodes>(),
        };

        var changes = new List<ProtocolChange>();
        Diff("EventCodes", app["EventCodes"], live["EventCodes"], changes);
        Diff("OperationCodes", app["OperationCodes"], live["OperationCodes"], changes);

        var fingerprint = Fingerprint(client.Version, live);
        return new ProtocolScanResult(true, null, client.Version, client.MetadataPath,
            fingerprint, lowConfidence, changes);
    }

    private static void Diff(string enumName,
        Dictionary<string, int> app, Dictionary<string, int> live, List<ProtocolChange> outChanges)
    {
        foreach (var (name, clientCode) in live)
        {
            if (!app.TryGetValue(name, out var appCode))
                outChanges.Add(new ProtocolChange(enumName, ProtocolChangeType.Added, name, null, clientCode));
            else if (appCode != clientCode)
                outChanges.Add(new ProtocolChange(enumName, ProtocolChangeType.Shifted, name, appCode, clientCode));
        }
        foreach (var (name, appCode) in app)
            if (!live.ContainsKey(name))
                outChanges.Add(new ProtocolChange(enumName, ProtocolChangeType.Removed, name, appCode, null));
    }

    private static Dictionary<string, int> EnumMap<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(v => v.ToString(), v => Convert.ToInt32(v));

    // Identifies a specific client protocol revision: version plus a hash of every live ordinal.
    // Unchanged across re-scans of the same client; changes the moment the game patches.
    private static string Fingerprint(string? version, Dictionary<string, Dictionary<string, int>> live)
    {
        ulong h = 1469598103934665603; // FNV-1a 64-bit
        void Mix(string s)
        {
            foreach (var ch in s) { h ^= ch; h *= 1099511628211; }
            h ^= '\n'; h *= 1099511628211;
        }

        Mix(version ?? "");
        foreach (var enumName in live.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Mix(enumName);
            foreach (var (name, code) in live[enumName].OrderBy(kv => kv.Value))
                Mix($"{name}={code}");
        }
        return h.ToString("x16");
    }
}
