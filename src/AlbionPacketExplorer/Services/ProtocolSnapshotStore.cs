using System.Text.Json;
using System.Text.Json.Serialization;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Persists a snapshot of the protocol enums (EventCodes / OperationCodes ordinals) for every
/// distinct client revision the app has seen, and binds a saved capture back to the exact snapshot
/// it was recorded under. Together these keep an OLD capture readable after a game patch renumbers
/// codes: the file keeps its raw wire codes, and on open they are remapped by NAME to the app's
/// current enums (see <see cref="ProtocolRemap"/>).
///
/// <para>Nothing here mutates a saved capture. Identity is carried in a sidecar
/// (<c>&lt;capture&gt;.apxmeta.json</c>) and resolution happens at read time. A pre-feature capture
/// with no sidecar falls back to the live overrides, i.e. today's behaviour.</para>
/// </summary>
public sealed class ProtocolSnapshotStore
{
    /// <summary>A protocol era: the enum ordinals plus the fingerprint that identifies them.</summary>
    public sealed record Snapshot(
        string Fingerprint,
        string? ClientVersion,
        Dictionary<string, Dictionary<string, int>> Enums);

    // Sidecar written next to a capture; the pointer to which era wrote it.
    private sealed record CaptureMeta(string Fingerprint, string? ClientVersion, string CapturedAt);
    // The era fresh captures are stamped with (last scanned client). Absent = use the app baseline.
    private sealed record ActivePointer(string Fingerprint, string? ClientVersion);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Dir => AppPaths.ProtocolSnapshotsDir;
    private static string ActiveFile => AppPaths.ProtocolActive;
    private static string SidecarOf(string capturePath) => capturePath + ".apxmeta.json";
    private static string FileOf(string fingerprint) => Path.Combine(Dir, fingerprint + ".json");

    // ---- writing snapshots ------------------------------------------------

    /// <summary>Saves the app's own compiled enums as a snapshot (idempotent). This is the era a
    /// capture is stamped with when no client scan has ever run - the app decodes with these.</summary>
    public void EnsureAppBaseline() => Save(AppBaseline());

    /// <summary>Records the live client's enums from a protocol scan and marks them the active era
    /// (what fresh captures are stamped with). No-op when the scan produced no enums.</summary>
    public void SaveLive(string? clientVersion, IReadOnlyDictionary<string, Dictionary<string, int>>? live)
    {
        if (live is null || live.Count == 0) return;
        var enums = Clone(live);
        var snap = new Snapshot(Fingerprint(enums), clientVersion, enums);
        Save(snap);
        WriteActive(new ActivePointer(snap.Fingerprint, snap.ClientVersion));
    }

    // ---- stamping + binding captures --------------------------------------

    /// <summary>Writes the era sidecar next to a just-saved capture so it can be re-bound later.
    /// Uses the active client era when a scan has run, else the app baseline. Best effort: an
    /// unstamped capture just falls back to the live overrides when opened.</summary>
    public void StampCapture(string capturePath)
    {
        try
        {
            var active = ReadActive();
            var (fp, ver) = active is not null
                ? (active.Fingerprint, active.ClientVersion)
                : (AppBaseline().Fingerprint, (string?)null);
            var meta = new CaptureMeta(fp, ver, DateTime.UtcNow.ToString("O"));
            File.WriteAllText(SidecarOf(capturePath), JsonSerializer.Serialize(meta, JsonOpts));
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Binds resolution to the era a capture was recorded under. When the sidecar names a snapshot we
    /// hold - and it differs from the app baseline - its codes are remapped by name to the app's
    /// current enums and pushed to the resolver + schema. Otherwise the live overrides are restored so
    /// the file reads under today's protocol (unchanged pre-feature behaviour). Returns true when an
    /// era binding was applied.
    /// </summary>
    public bool BindCaptureEra(string capturePath)
    {
        var meta = ReadSidecar(capturePath);
        if (meta is null || meta.Fingerprint == AppBaseline().Fingerprint)
        {
            RestoreLive();
            return false;
        }

        var era = TryLoad(meta.Fingerprint);
        if (era is null)
        {
            RestoreLive();
            return false;
        }

        var (names, aliases) = ProtocolRemap.Build(era.Enums, AppBaseline().Enums);
        PacketNameResolver.SetOverrides(names);
        PacketSchemaService.SetCodeAliases(aliases);
        return true;
    }

    /// <summary>Reapplies the live protocol overrides (post-patch scan results), replacing any era
    /// binding a previously opened capture left active.</summary>
    public void RestoreLive() => new ProtocolOverrideStore().Load();

    // ---- normalization (cross-era merge) ----------------------------------

    /// <summary>The era a capture was stamped with, or null when it has no sidecar.</summary>
    public Snapshot? EraFromSidecar(string capturePath)
    {
        var meta = ReadSidecar(capturePath);
        return meta is null ? null : TryLoad(meta.Fingerprint);
    }

    /// <summary>Best-effort era for an unstamped capture, from the codes it contains.</summary>
    public Snapshot? DetectEra(IReadOnlyCollection<(string Kind, int Code)> observed)
        => DetectEra(LoadAll(), AppBaseline(), observed);

    /// <summary>
    /// Picks the era a capture was recorded under from the distinct codes it uses. Conservative on
    /// purpose: if the app's current enums already define every observed code it is treated as the
    /// current era (returns null, no remap); a remap fires only when the current enums CANNOT explain
    /// some code (a removed/shifted ordinal leaves a hole) and an older snapshot explains all of them.
    /// A pure rename with no removed codes is indistinguishable from current and left as-is.
    /// </summary>
    public static Snapshot? DetectEra(
        IReadOnlyList<Snapshot> candidates, Snapshot canonical,
        IReadOnlyCollection<(string Kind, int Code)> observed)
    {
        if (observed.Count == 0) return null;
        int Cover(Snapshot s) => observed.Count(o => Defines(s, o.Kind, o.Code));

        // Current enums explain everything -> current era, nothing to normalize.
        if (Cover(canonical) == observed.Count) return null;

        Snapshot? best = null;
        int bestCover = -1;
        foreach (var s in candidates)
        {
            if (s.Fingerprint == canonical.Fingerprint) continue;
            int c = Cover(s);
            if (c > bestCover) { bestCover = c; best = s; }
        }
        // Only act on a confident hit: the era must explain every observed code.
        return best is not null && bestCover == observed.Count ? best : null;
    }

    private static bool Defines(Snapshot s, string kind, int code)
    {
        var enumName = kind.Equals("EVENT", StringComparison.OrdinalIgnoreCase) ? "EventCodes" : "OperationCodes";
        return s.Enums.TryGetValue(enumName, out var map) && map.ContainsValue(code);
    }

    /// <summary>The (kind, wireCode) -> canonical code map that normalizes an era to the app's current
    /// enums. Empty when the era already matches the app baseline.</summary>
    public static Dictionary<(string Kind, int Code), int> CanonicalRemap(Snapshot era)
        => ProtocolRemap.Build(era.Enums, AppBaseline().Enums).Aliases;

    /// <summary>Every stored era snapshot plus the app baseline (first, so detection ties keep it).</summary>
    public IReadOnlyList<Snapshot> LoadAll()
    {
        var list = new List<Snapshot> { AppBaseline() };
        try
        {
            if (Directory.Exists(Dir))
                foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
                {
                    try
                    {
                        var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(f), JsonOpts);
                        if (s is not null && list.All(x => x.Fingerprint != s.Fingerprint)) list.Add(s);
                    }
                    catch { /* skip a corrupt snapshot */ }
                }
        }
        catch { }
        return list;
    }

    /// <summary>Stamp a normalized output as the canonical (app baseline) era.</summary>
    public void StampCanonical(string capturePath)
    {
        try
        {
            var meta = new CaptureMeta(AppBaseline().Fingerprint, null, DateTime.UtcNow.ToString("O"));
            File.WriteAllText(SidecarOf(capturePath), JsonSerializer.Serialize(meta, JsonOpts));
        }
        catch { }
    }

    // ---- snapshot persistence ---------------------------------------------

    public Snapshot? TryLoad(string fingerprint)
    {
        try
        {
            var path = FileOf(fingerprint);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), JsonOpts);
        }
        catch { return null; }
    }

    private static void Save(Snapshot snap)
    {
        try
        {
            var path = FileOf(snap.Fingerprint);
            if (File.Exists(path)) return; // a fingerprint fully defines its snapshot: never rewrite
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    private static CaptureMeta? ReadSidecar(string capturePath)
    {
        try
        {
            var p = SidecarOf(capturePath);
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<CaptureMeta>(File.ReadAllText(p), JsonOpts);
        }
        catch { return null; }
    }

    private static ActivePointer? ReadActive()
    {
        try
        {
            if (!File.Exists(ActiveFile)) return null;
            return JsonSerializer.Deserialize<ActivePointer>(File.ReadAllText(ActiveFile), JsonOpts);
        }
        catch { return null; }
    }

    private static void WriteActive(ActivePointer ptr)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ActiveFile)!);
            File.WriteAllText(ActiveFile, JsonSerializer.Serialize(ptr, JsonOpts));
        }
        catch { }
    }

    // ---- building snapshots from enums ------------------------------------

    /// <summary>Snapshot of the app's own compiled protocol enums.</summary>
    public static Snapshot AppBaseline()
    {
        var enums = new Dictionary<string, Dictionary<string, int>>
        {
            ["EventCodes"] = EnumMap<EventCodes>(),
            ["OperationCodes"] = EnumMap<OperationCodes>(),
        };
        return new Snapshot(Fingerprint(enums), null, enums);
    }

    private static Dictionary<string, int> EnumMap<TEnum>() where TEnum : struct, Enum
    {
        var map = new Dictionary<string, int>();
        foreach (var v in Enum.GetValues<TEnum>())
            map[v.ToString()] = Convert.ToInt32(v); // names are unique; a dup name overwrites identically
        return map;
    }

    /// <summary>Order-independent FNV-1a hash of every (enum, name=code) triple. Identical protocols
    /// hash identically regardless of the client version string, so a scan and the app baseline of the
    /// same protocol share a fingerprint.</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, Dictionary<string, int>> enums)
    {
        ulong h = 1469598103934665603UL;
        void Mix(string s)
        {
            foreach (var ch in s) { h ^= ch; h *= 1099511628211UL; }
            h ^= '\n'; h *= 1099511628211UL;
        }

        foreach (var enumName in enums.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Mix(enumName);
            foreach (var kv in enums[enumName].OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                Mix($"{kv.Key}={kv.Value}");
        }
        return h.ToString("x16");
    }

    private static Dictionary<string, Dictionary<string, int>> Clone(
        IReadOnlyDictionary<string, Dictionary<string, int>> src)
    {
        var outer = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (k, inner) in src)
            outer[k] = new Dictionary<string, int>(inner);
        return outer;
    }
}
