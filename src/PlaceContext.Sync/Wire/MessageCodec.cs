namespace PlaceContext.Sync.Wire;

/// <summary>
/// Encodes/decodes a <see cref="Message"/> to/from its frame payload (<c>u8(kind) body</c>), and
/// wraps it in a length-prefixed frame. Stateless; see docs/SYNC-PROTOCOL.md §4–§5.
/// </summary>
public static class MessageCodec
{
    /// <summary>Encode a message as a complete frame: <c>uvarint(len) payload</c>.</summary>
    public static byte[] EncodeFrame(Message message)
    {
        var payload = EncodePayload(message);
        var w = new WireWriter();
        w.UVarint(payload.Length);
        var prefix = w.ToArray();
        var frame = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, frame, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, frame, prefix.Length, payload.Length);
        return frame;
    }

    /// <summary>
    /// Try to read one frame from the front of <paramref name="buffer"/>. Returns false (with
    /// <paramref name="consumed"/> = 0) when the buffer does not yet hold a whole frame; the caller
    /// should read more bytes and retry. Advances <paramref name="consumed"/> past the frame on success.
    /// </summary>
    public static bool TryReadFrame(ReadOnlySpan<byte> buffer, out Message? message, out int consumed)
    {
        message = null;
        consumed = 0;

        // Parse the length prefix without throwing on a short buffer.
        ulong len = 0;
        int shift = 0, i = 0;
        while (true)
        {
            if (i >= buffer.Length) return false;            // prefix incomplete
            if (shift > 63) throw new WireFormatException("Frame length overflow.");
            byte b = buffer[i++];
            len |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }

        if ((ulong)(buffer.Length - i) < len) return false;  // body not fully arrived

        var payload = buffer.Slice(i, (int)len);
        message = DecodePayload(payload);
        consumed = i + (int)len;
        return true;
    }

    public static byte[] EncodePayload(Message message)
    {
        var w = new WireWriter();
        w.U8((byte)message.Kind);
        switch (message)
        {
            case HelloMessage h:
                w.UVarint(h.Version.Major);
                w.UVarint(h.Version.Minor);
                w.String(h.NodeId.Value);
                w.UVarint(h.Capabilities.Count);
                foreach (var cap in h.Capabilities) w.String(cap);
                WriteClock(w, h.Frontier);
                break;
            case PushMessage p:
                w.UVarint(p.Records.Count);
                foreach (var r in p.Records) WriteRecord(w, r);
                break;
            case AckMessage a:
                WriteClock(w, a.Frontier);
                break;
            case ByeMessage b:
                w.String(b.Reason);
                break;
            case ChatMessage c:
                w.String(c.Text);
                w.UVarint(c.SentAtUnixMs);
                break;
            default:
                throw new WireFormatException($"Unknown message type {message.GetType().Name}.");
        }
        return w.ToArray();
    }

    public static Message DecodePayload(ReadOnlySpan<byte> payload)
    {
        var r = new WireReader(payload);
        var kind = (MessageKind)r.U8();
        switch (kind)
        {
            case MessageKind.Hello:
            {
                var version = new ProtocolVersion((int)r.UVarint(), (int)r.UVarint());
                var nodeId = NodeId.Parse(r.String());
                var capCount = r.UVarint();
                var caps = new List<string>((int)capCount);
                for (long i = 0; i < capCount; i++) caps.Add(r.String());
                var frontier = ReadClock(ref r);
                return new HelloMessage(version, nodeId, caps, frontier);
            }
            case MessageKind.Push:
            {
                var count = r.UVarint();
                var records = new List<SyncRecord>((int)count);
                for (long i = 0; i < count; i++) records.Add(ReadRecord(ref r));
                return new PushMessage(records);
            }
            case MessageKind.Ack:
                return new AckMessage(ReadClock(ref r));
            case MessageKind.Bye:
                return new ByeMessage(r.String());
            case MessageKind.Chat:
                return new ChatMessage(r.String(), r.UVarint());
            default:
                throw new WireFormatException($"Unknown message kind {(byte)kind}.");
        }
    }

    private static void WriteClock(WireWriter w, VectorClock clock)
    {
        var entries = clock.Entries;
        w.UVarint(entries.Count);
        foreach (var (node, seq) in entries)
        {
            w.String(node);
            w.UVarint(seq);
        }
    }

    private static VectorClock ReadClock(ref WireReader r)
    {
        var count = r.UVarint();
        var acc = VectorClock.Empty;
        for (long i = 0; i < count; i++)
        {
            var node = NodeId.Parse(r.String());
            var seq = r.UVarint();
            acc = acc.Observe(node, seq);
        }
        return acc;
    }

    private static void WriteRecord(WireWriter w, SyncRecord record)
    {
        w.String(record.Origin.Value);
        w.UVarint(record.Sequence);
        w.Guid(record.ProjectId);
        w.String(record.Kind);
        w.String(record.Digest);
        w.Bytes(record.Payload);
    }

    private static SyncRecord ReadRecord(ref WireReader r)
    {
        var origin = NodeId.Parse(r.String());
        var sequence = r.UVarint();
        var projectId = r.Guid();
        var kind = r.String();
        var digest = r.String();
        var payload = r.Bytes();
        return SyncRecord.Create(origin, sequence, projectId, kind, digest, payload);
    }
}
