using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>
/// Builds a param-by-param diff between two packets. Shared by the detail pane's request/response
/// Diff tab and the standalone two-row diff window, so both colour and align fields identically.
/// </summary>
public static class PacketDiff
{
    // Photon transport echoes the code at key 252 (events) / 253 (requests, responses); neither is
    // payload, so they never make a meaningful diff row.
    private static readonly HashSet<string> EchoKeys = ["252", "253"];

    public static List<ParamDiffRow> Build(PacketEntry left, PacketEntry right, PacketSchemaService schema)
    {
        var keys = left.Params.Keys
            .Union(right.Params.Keys)
            .Where(k => !EchoKeys.Contains(k))
            .OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue);

        var rows = new List<ParamDiffRow>();
        foreach (var key in keys)
        {
            // ParamValue is a struct, so presence is the bool TryGetValue returns, not a null check.
            bool hasLeft = left.Params.TryGetValue(key, out var a);
            bool hasRight = right.Params.TryGetValue(key, out var b);

            var va = hasLeft ? PacketDisplayFormatter.FormatParamValue(a) : string.Empty;
            var vb = hasRight ? PacketDisplayFormatter.FormatParamValue(b) : string.Empty;
            var status = !hasLeft ? "right"
                       : !hasRight ? "left"
                       : va == vb  ? "same"
                       :             "changed";

            var name = schema.GetParam(left.Kind, left.Code, key)?.Name
                     ?? schema.GetParam(right.Kind, right.Code, key)?.Name
                     ?? string.Empty;

            rows.Add(new ParamDiffRow
            {
                Key = key, Name = name, LeftValue = va, RightValue = vb, Status = status,
            });
        }
        return rows;
    }
}
