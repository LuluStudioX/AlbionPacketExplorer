using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Session-scoped, LEARNED entity-name resolver. Photon events reference other entities by a
/// transient numeric <c>entityId</c> (assigned when the object spawns in the client's local scene),
/// not by name - so a Move/HealthUpdate/Attack event carries a bare id like <c>1379287</c>, useless
/// on its own. A small set of "spawn" events, though, carry BOTH that id and a readable name; this
/// observes them as a capture loads (or streams live) and builds an <c>id -&gt; name</c> map, so a
/// param schema-tagged <c>resolveAs "entity"|"player"</c> shows the name learned from THIS capture.
///
/// <para>Unlike <see cref="GameRefStore"/> (static embedded reference data), this is populated at
/// runtime from the observed packets and is cleared on <see cref="Reset"/> when a new dataset loads,
/// exactly like <see cref="PacketCorrelator"/>. Feed every packet through <see cref="Observe"/> in
/// capture order; resolution reads the accumulated map.</para>
///
/// <para>Ceiling: entity ids are per-scene and CAN be recycled within a long capture (an id freed by
/// a despawn may later name a different object). Resolution is therefore last-write-wins - the most
/// recent naming of an id. For the short captures this tool inspects that is accurate; a multi-hour
/// capture spanning map changes could show a stale name for a recycled id. Adding a naming event is
/// one <see cref="Source"/> line.</para>
/// </summary>
public sealed class EntityNameStore
{
    // A naming event: its EVENT code, the param key holding the numeric entityId, the param key
    // holding the readable name, and the domain the pair belongs to. Verified against live captures.
    private readonly record struct Source(int Code, string IdKey, string NameKey, string Domain);

    private static readonly Source[] Sources =
    [
        // NewCharacter (29): param 0 = entityId (Int64), param 1 = character name. The canonical
        // player-id naming every Albion event references (move/combat/health source & target ids).
        new Source(29, "0", "1", "player"),
    ];

    // The special resolveAs that resolves against EVERY learned domain (an entityId of unknown kind).
    public const string AnyDomain = "entity";

    private static readonly string[] Domains =
        Sources.Select(s => s.Domain).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToArray();

    // domain -> (entityId as string -> learned name). Seeded with every known domain so the resolve
    // surface and the param-editor options are stable before anything has been observed.
    private readonly Dictionary<string, Dictionary<string, string>> _byDomain =
        Domains.ToDictionary(d => d, _ => new Dictionary<string, string>(), StringComparer.Ordinal);

    /// <summary>Learns id -&gt; name from a packet if it is one of the naming events; else a no-op.
    /// Params are decoded ONLY for a matching event code, so the 99.99% non-naming packets pay just a
    /// tiny code compare (never a param decode) on the load hot path.</summary>
    public void Observe(PacketEntry packet)
    {
        if (packet.Kind != "EVENT") return;

        Source src = default;
        bool matched = false;
        foreach (var s in Sources)
            if (s.Code == packet.Code) { src = s; matched = true; break; }
        if (!matched) return;

        var ps = packet.Params;
        if (!ps.TryGetValue(src.IdKey, out var idv) || !ps.TryGetValue(src.NameKey, out var nameV))
            return;
        if (nameV.Value is not string name || string.IsNullOrEmpty(name)) return;
        var id = IdToString(idv.Value);
        if (id is null) return;

        _byDomain[src.Domain][id] = name; // last-write-wins (see ceiling in the type doc)
    }

    /// <summary>Drops every learned name so a freshly loaded dataset starts clean (called from
    /// ResetData alongside the correlator reset).</summary>
    public void Reset()
    {
        foreach (var map in _byDomain.Values) map.Clear();
    }

    /// <summary>True when <paramref name="resolveAs"/> names the any-domain tag or a learned domain.</summary>
    public bool IsEntityResolve(string resolveAs)
        => resolveAs == AnyDomain || _byDomain.ContainsKey(resolveAs);

    /// <summary>Resolves an entityId (as string) to a learned name; false when nothing was learned for
    /// it. <c>"entity"</c> searches every domain; a domain tag searches only that domain.</summary>
    public bool TryResolve(string resolveAs, string code, out string name)
    {
        name = string.Empty;
        if (resolveAs == AnyDomain)
        {
            foreach (var map in _byDomain.Values)
                if (map.TryGetValue(code, out name!)) return true;
            return false;
        }
        return _byDomain.TryGetValue(resolveAs, out var m) && m.TryGetValue(code, out name!);
    }

    /// <summary>resolveAs options for the param editor: the any-domain tag plus each learned domain.</summary>
    public IEnumerable<string> ResolveAsOptions() => new[] { AnyDomain }.Concat(Domains);

    /// <summary>Total learned id -&gt; name pairs across all domains (for status/diagnostics).</summary>
    public int Count => _byDomain.Values.Sum(m => m.Count);

    // Entity ids arrive as Int64 (widened) on load and int/short live; stringify with the invariant
    // culture so the key matches the value the resolver reads from the referencing param (which also
    // stringifies via ToString). Non-integral payloads (a GUID Byte[]) are not entity ids -> null.
    private static string? IdToString(object? value) => value switch
    {
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        short s => s.ToString(System.Globalization.CultureInfo.InvariantCulture),
        byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };
}
