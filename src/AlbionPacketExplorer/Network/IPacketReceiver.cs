namespace AlbionPacketExplorer.Network;

/// <summary>Sink for raw UDP payloads pulled off the wire by a <see cref="PacketProvider"/>.
/// Implemented by the decoder so the capture layer stays decoupled from how packets are parsed.</summary>
public interface IPacketReceiver
{
    void ReceivePacket(byte[] payload);
}
