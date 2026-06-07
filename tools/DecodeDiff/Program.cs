using System.Globalization;
using AlbionPacketExplorer.PhotonPackageParser;
using AlbionPacketExplorer.PhotonWire;

// Replays a raw-packet capture (base64 per line, from APX_SAVE_RAW) through the old GPL decoder and
// the new independent PhotonWire decoder, and reports where they disagree. Real parity check for
// the decode rewrite (stage 4). Usage: dotnet run --project tools/DecodeDiff -- <raw.b64>

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: DecodeDiff <raw-packets.b64>");
    return 1;
}

int pkt = 0, oldMsgs = 0, newMsgs = 0, mismatch = 0, oldErr = 0, newErr = 0, shown = 0;

foreach (var line in File.ReadLines(args[0]))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    byte[] payload;
    try { payload = Convert.FromBase64String(line.Trim()); }
    catch { continue; }
    pkt++;

    var old = new OldCollector();
    try { old.ReceivePacket(payload); } catch { oldErr++; }

    var neu = new List<(string Kind, int Code, IReadOnlyDictionary<byte, object?> P)>();
    var reader = new PhotonPacketReader();
    reader.OnEvent += e => neu.Add(("EVENT", e.Code, e.Parameters));
    reader.OnRequest += e => neu.Add(("REQUEST", e.OperationCode, e.Parameters));
    reader.OnResponse += e => neu.Add(("RESPONSE", e.OperationCode, e.Parameters));
    try { reader.ReadPacket(payload); } catch { newErr++; }

    oldMsgs += old.Msgs.Count;
    newMsgs += neu.Count;

    if (old.Msgs.Count != neu.Count)
    {
        mismatch++;
        if (shown++ < 25) Console.WriteLine($"pkt {pkt}: message count old={old.Msgs.Count} new={neu.Count}");
        continue;
    }
    for (var i = 0; i < old.Msgs.Count; i++)
    {
        var o = old.Msgs[i];
        var n = neu[i];
        if (o.Kind != n.Kind || !ParamsEqual(o.P, n.P))
        {
            mismatch++;
            if (shown++ < 25)
                Console.WriteLine($"pkt {pkt} msg {i}: OLD {o.Kind} {{{Fmt(o.P)}}}  vs  NEW {n.Kind} {{{Fmt(n.P)}}}");
        }
    }
}

Console.WriteLine($"\npackets={pkt}  oldMsgs={oldMsgs}  newMsgs={newMsgs}  mismatches={mismatch}  oldErr={oldErr}  newErr={newErr}");
return mismatch == 0 && newErr == 0 ? 0 : 2;

static bool ParamsEqual(IReadOnlyDictionary<byte, object?> a, IReadOnlyDictionary<byte, object?> b)
{
    if (a.Count != b.Count) return false;
    foreach (var (k, v) in a)
        if (!b.TryGetValue(k, out var w) || Val(v) != Val(w)) return false;
    return true;
}

static string Fmt(IReadOnlyDictionary<byte, object?> p) =>
    string.Join(" ", p.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={Val(kv.Value)}"));

static string Val(object? v) => v switch
{
    null => "null",
    byte[] b => Convert.ToBase64String(b),
    string s => s,
    System.Collections.IEnumerable e => "[" + string.Join(",", e.Cast<object?>().Select(Val)) + "]",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};

// Collects the old decoder's messages with the same shape as the new one.
internal sealed class OldCollector : PhotonParser
{
    public readonly List<(string Kind, int Code, IReadOnlyDictionary<byte, object?> P)> Msgs = new();

    private static IReadOnlyDictionary<byte, object?> Box(Dictionary<byte, object> p)
        => p.ToDictionary(kv => kv.Key, kv => (object?) kv.Value);

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
        => Msgs.Add(("EVENT", code, Box(parameters)));

    protected override void OnRequest(byte operationCode, Dictionary<byte, object> parameters)
        => Msgs.Add(("REQUEST", operationCode, Box(parameters)));

    protected override void OnResponse(byte operationCode, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
        => Msgs.Add(("RESPONSE", operationCode, Box(parameters)));
}
