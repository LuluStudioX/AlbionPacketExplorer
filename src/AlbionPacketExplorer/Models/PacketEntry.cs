namespace AlbionPacketExplorer.Models;

public record PacketEntry(
    DateTime Timestamp,
    string Kind,
    int Code,
    Dictionary<string, ParamValue> Params,
    // Photon OperationResponse framing fields (RESPONSE only; null on EVENT/REQUEST).
    // ReturnCode 0 = success, non-zero = server-side failure; DebugMessage is the
    // server's optional error text. Decoded by PhotonWire's GpBinaryReader.ReadResponse.
    int? ReturnCode = null,
    string? DebugMessage = null)
{
    public int KeyCount => Params.Count;
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
