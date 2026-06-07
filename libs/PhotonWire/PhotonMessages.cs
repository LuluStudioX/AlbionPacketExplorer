namespace AlbionPacketExplorer.PhotonWire;

/// <summary>A decoded Photon message. Parameters are a byte-keyed map of GpBinary values.</summary>
public sealed record PhotonEvent(byte Code, Dictionary<byte, object?> Parameters);

public sealed record PhotonRequest(byte OperationCode, Dictionary<byte, object?> Parameters);

public sealed record PhotonResponse(
    byte OperationCode,
    short ReturnCode,
    string? DebugMessage,
    Dictionary<byte, object?> Parameters);
