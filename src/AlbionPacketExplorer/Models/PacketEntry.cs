namespace AlbionPacketExplorer.Models;

/// <summary>
/// A decoded packet. Its params are NOT held as live objects: instead the entry keeps a reference to
/// the shared <see cref="PackedParamStore"/> plus a <see cref="ParamRef"/> locating its raw
/// params-JSON bytes in that arena, and <see cref="Params"/> decodes them lazily on access. This
/// keeps ~4M packets from each retaining a per-packet param array; the surface is unchanged because
/// <see cref="Params"/> still returns a <see cref="ParamSet"/>.
/// </summary>
public record PacketEntry(
    DateTime Timestamp,
    string Kind,
    int Code,
    PackedParamStore Store,
    ParamRef ParamRef,
    // Photon OperationResponse framing fields (RESPONSE only; null on EVENT/REQUEST).
    // ReturnCode 0 = success, non-zero = server-side failure; DebugMessage is the
    // server's optional error text. Decoded by PhotonWire's GpBinaryReader.ReadResponse.
    int? ReturnCode = null,
    string? DebugMessage = null)
{
    /// <summary>
    /// The packet's params, decoded on demand from the packed store. Every reader uses this exactly
    /// as before (it is an <see cref="IReadOnlyDictionary{TKey,TValue}"/> surface); the decode runs
    /// only when a row is actually viewed/exported/filtered, never eagerly for all rows.
    /// </summary>
    public ParamSet Params => Store.Decode(ParamRef);

    /// <summary>Param count, read straight from the stored ref so the grid's Keys column never forces a decode.</summary>
    public int KeyCount => ParamRef.Count;
    public string ResolvedSummary { get; set; } = string.Empty;

    /// <summary>True when this packet carries Photon response status (RESPONSE kind).</summary>
    public bool HasResponseStatus => ReturnCode.HasValue;

    /// <summary>
    /// The paired packet for a REQUEST/RESPONSE exchange: a REQUEST points at its RESPONSE and
    /// vice versa. Photon shares the OperationCode between the two; <see cref="PacketCorrelator"/>
    /// matches them in arrival order. Null for EVENTs and any unmatched op. Never serialized.
    /// </summary>
    public PacketEntry? Correlated { get; set; }

    /// <summary>Shared id for a correlated REQUEST/RESPONSE pair (null until matched).</summary>
    public int? CorrelationId { get; set; }
}
