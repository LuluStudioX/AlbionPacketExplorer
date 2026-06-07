using System.Buffers.Binary;

namespace AlbionPacketExplorer.PhotonWire;

/// <summary>
/// Independent reader for Photon's reliable-UDP packet framing: a 12-byte packet header followed by
/// N commands; reliable/unreliable "send" commands carry a message (a one-byte message type + a
/// GpBinary body), and fragment commands are reassembled by start sequence. Decoded messages are
/// raised via the events below. Implemented from the protocol structure, not from any GPL source.
///
/// STATUS: build-alongside, NOT wired in, NOT byte-verified. Header/command sizes, the reliable
/// payload's leading byte, and the fragment field layout are marked VERIFY and must be confirmed
/// against real captured packets before this replaces the current framing.
/// </summary>
public sealed class PhotonPacketReader
{
    public event Action<PhotonEvent>? OnEvent;
    public event Action<PhotonRequest>? OnRequest;
    public event Action<PhotonResponse>? OnResponse;

    private const int PacketHeaderLength = 12;   // VERIFY: peerId(2)+flags(1)+cmdCount(1)+time(4)+challenge(4)
    private const int CommandHeaderLength = 12;  // VERIFY: type(1)+channel(1)+flags(1)+reserved(1)+len(4)+relSeq(4)

    // ENet/Photon command types (protocol facts).
    private const byte CmdAck = 1;
    private const byte CmdConnect = 2;
    private const byte CmdVerifyConnect = 3;
    private const byte CmdDisconnect = 4;
    private const byte CmdSendReliable = 6;
    private const byte CmdSendUnreliable = 7;
    private const byte CmdSendFragment = 8;

    // Photon message types.
    private const byte MsgOperationRequest = 2;
    private const byte MsgOperationResponse = 3;
    private const byte MsgEvent = 4;

    private readonly Dictionary<int, Fragment> _fragments = new();

    public void ReadPacket(byte[] data)
    {
        if (data.Length < PacketHeaderLength) return;
        var pos = 0;
        // header: peerId(2), flags(1), commandCount(1), serverTime(4), challenge(4)
        pos += 2;                       // peerId
        pos += 1;                       // flags / crc
        var commandCount = data[pos++];
        pos += 4;                       // serverTime
        pos += 4;                       // challenge

        for (var i = 0; i < commandCount && pos + CommandHeaderLength <= data.Length; i++)
        {
            var cmdStart = pos;
            var type = data[pos];
            // command header: type(1), channelId(1), flags(1), reserved(1), length(4), relSeq(4)
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(cmdStart + 4));
            if (length < CommandHeaderLength || cmdStart + length > data.Length) return;

            var bodyStart = cmdStart + CommandHeaderLength;
            var bodyLen = length - CommandHeaderLength;
            pos = cmdStart + length;    // advance to next command regardless of type

            switch (type)
            {
                case CmdSendReliable:
                    HandleMessage(data, bodyStart, bodyLen, leadingSkip: 1);
                    break;
                case CmdSendUnreliable:
                    // 4 extra bytes (unreliable sequence number) precede the reliable-style body.
                    HandleMessage(data, bodyStart + 4, bodyLen - 4, leadingSkip: 1);
                    break;
                case CmdSendFragment:
                    HandleFragment(data, bodyStart, bodyLen);
                    break;
                case CmdAck or CmdConnect or CmdVerifyConnect or CmdDisconnect:
                default:
                    break; // not payload-bearing
            }
        }
    }

    // A send body: optional leading byte(s), then a message-type byte, then a GpBinary message.
    private void HandleMessage(byte[] data, int start, int len, int leadingSkip)
    {
        if (len <= leadingSkip + 1) return;
        var p = start + leadingSkip;          // VERIFY: leading byte before the message type
        var msgType = data[p++];
        var reader = new GpBinaryReader(data, p);
        switch (msgType)
        {
            case MsgEvent:             OnEvent?.Invoke(reader.ReadEvent()); break;
            case MsgOperationRequest:  OnRequest?.Invoke(reader.ReadRequest()); break;
            case MsgOperationResponse: OnResponse?.Invoke(reader.ReadResponse()); break;
        }
    }

    // VERIFY: fragment field layout (startSeq, count, number, totalLength, offset), then bytes.
    private void HandleFragment(byte[] data, int start, int len)
    {
        if (len < 20) return;
        var s = data.AsSpan(start);
        var startSeq = BinaryPrimitives.ReadInt32BigEndian(s);
        var fragmentCount = BinaryPrimitives.ReadInt32BigEndian(s[4..]);
        var fragmentNumber = BinaryPrimitives.ReadInt32BigEndian(s[8..]);
        var totalLength = BinaryPrimitives.ReadInt32BigEndian(s[12..]);
        var fragmentOffset = BinaryPrimitives.ReadInt32BigEndian(s[16..]);
        var payloadStart = start + 20;
        var payloadLen = len - 20;
        if (payloadLen <= 0 || totalLength <= 0 || fragmentOffset < 0 || fragmentOffset + payloadLen > totalLength)
            return;

        if (!_fragments.TryGetValue(startSeq, out var frag))
            _fragments[startSeq] = frag = new Fragment(totalLength, fragmentCount);

        Array.Copy(data, payloadStart, frag.Buffer, fragmentOffset, payloadLen);
        frag.Received++;
        if (frag.Received < frag.Count) return;

        _fragments.Remove(startSeq);
        // Reassembled buffer is a reliable-style send body (leading byte + msgType + GpBinary).
        HandleMessage(frag.Buffer, 0, frag.Buffer.Length, leadingSkip: 1);
    }

    private sealed class Fragment(int totalLength, int count)
    {
        public readonly byte[] Buffer = new byte[totalLength];
        public readonly int Count = count;
        public int Received;
    }
}
