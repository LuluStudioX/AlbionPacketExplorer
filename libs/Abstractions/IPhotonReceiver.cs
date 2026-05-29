using System.Buffers;

namespace AlbionPacketExplorer.Abstractions;

public interface IPhotonReceiver
{
    void ReceivePacket(byte[] payload);
    void ReceivePacket(ReadOnlySpan<byte> payload);
    void ReceivePacket(ReadOnlySequence<byte> payload);
}
