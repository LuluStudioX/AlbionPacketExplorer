using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Single source of truth for the on-the-wire JSON shape of a packet ({ts, kind, code,
/// returnCode?, debugMessage?, params}). Every save / export / clipboard path projects
/// through here so the format never drifts between call sites. returnCode/debugMessage are
/// emitted only when present (RESPONSE packets), keeping EVENT/REQUEST output unchanged.
/// </summary>
public static class PacketWire
{
    public static Dictionary<string, object?> ToJsonShape(PacketEntry p) => ToJsonShape(p, p.Params);

    /// <summary>
    /// Shape a packet using an explicitly supplied param set. The merge path passes the ParamSet the
    /// reader just parsed, so serialization never re-decodes the mmap store - and because the set is
    /// an immutable value, many packets can be shaped in parallel.
    /// </summary>
    public static Dictionary<string, object?> ToJsonShape(PacketEntry p, ParamSet ps)
    {
        var wire = new Dictionary<string, object?>(6)
        {
            ["ts"] = p.Timestamp,
            ["kind"] = p.Kind,
            ["code"] = p.Code,
        };
        if (p.ReturnCode is { } rc) wire["returnCode"] = rc;
        if (!string.IsNullOrEmpty(p.DebugMessage)) wire["debugMessage"] = p.DebugMessage;
        wire["params"] = ps.ToDictionary(
            kv => kv.Key,
            kv => (object)new { type = kv.Value.Type, value = kv.Value.Value });
        return wire;
    }

    /// <summary>
    /// Shape a packet with its opcode rewritten to a canonical value (era normalization for merge).
    /// Only the top-level <c>code</c> changes; params - including the 253/252 wire-code mirror - stay
    /// verbatim as the original raw payload, so the pre-normalization code is still recoverable.
    /// </summary>
    public static Dictionary<string, object?> ToJsonShape(PacketEntry p, ParamSet ps, int code)
    {
        var wire = ToJsonShape(p, ps);
        wire["code"] = code;
        return wire;
    }
}
