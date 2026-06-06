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
    public static Dictionary<string, object?> ToJsonShape(PacketEntry p)
    {
        var wire = new Dictionary<string, object?>(6)
        {
            ["ts"] = p.Timestamp,
            ["kind"] = p.Kind,
            ["code"] = p.Code,
        };
        if (p.ReturnCode is { } rc) wire["returnCode"] = rc;
        if (!string.IsNullOrEmpty(p.DebugMessage)) wire["debugMessage"] = p.DebugMessage;
        wire["params"] = p.Params.ToDictionary(
            kv => kv.Key,
            kv => (object)new { type = kv.Value.Type, value = kv.Value.Value });
        return wire;
    }
}
