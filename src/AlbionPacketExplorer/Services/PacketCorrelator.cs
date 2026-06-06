using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Pairs each REQUEST with its matching RESPONSE. Photon's base protocol carries no request id,
/// so correlation is by shared OperationCode in arrival order: a RESPONSE matches the oldest
/// still-unmatched REQUEST of the same code (FIFO per code). Feed packets in capture order via
/// <see cref="Observe"/>; matched pairs get a shared <see cref="PacketEntry.CorrelationId"/> and
/// cross-linked <see cref="PacketEntry.Correlated"/> references.
///
/// A REQUEST that never gets a captured response would otherwise linger in the queue and later be
/// falsely paired with an unrelated response that merely reuses the opcode (seen as pairs minutes
/// or hours apart). Photon op round-trips are sub-second, so pairing is capped to
/// <see cref="MaxPairingWindow"/>: stale requests older than that are dropped before matching.
/// </summary>
public sealed class PacketCorrelator
{
    // Generous upper bound on a real request->response round-trip. Anything beyond this is treated
    // as "the request got no response" rather than a match (game RTT is milliseconds).
    private static readonly TimeSpan MaxPairingWindow = TimeSpan.FromSeconds(30);

    private readonly Dictionary<int, Queue<PacketEntry>> _pendingRequests = new();
    private int _nextId = 1;

    public void Observe(PacketEntry packet)
    {
        switch (packet.Kind)
        {
            case "REQUEST":
                PendingFor(packet.Code).Enqueue(packet);
                break;
            case "RESPONSE":
                MatchResponse(packet);
                break;
        }
    }

    public void Reset()
    {
        _pendingRequests.Clear();
        _nextId = 1;
    }

    private void MatchResponse(PacketEntry response)
    {
        if (!_pendingRequests.TryGetValue(response.Code, out var queue))
            return;

        // Discard unmatched requests too old to belong to this response (they never got one),
        // so a much-later response reusing the opcode can't be paired with a stale request.
        while (queue.Count > 0 && response.Timestamp - queue.Peek().Timestamp > MaxPairingWindow)
            queue.Dequeue();

        if (queue.Count == 0)
            return;

        var request = queue.Dequeue();
        var id = request.CorrelationId ??= _nextId++;
        response.CorrelationId = id;
        request.Correlated = response;
        response.Correlated = request;
    }

    private Queue<PacketEntry> PendingFor(int code)
    {
        if (!_pendingRequests.TryGetValue(code, out var queue))
            _pendingRequests[code] = queue = new Queue<PacketEntry>();
        return queue;
    }
}
