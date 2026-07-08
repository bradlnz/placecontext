namespace PlaceContext.Sync;

/// <summary>The wire discriminator for a PCSP message (the first byte of a frame payload).</summary>
public enum MessageKind : byte
{
    Hello = 1,
    Push = 2,
    Ack = 3,
    Bye = 4,
    Chat = 5,
}

/// <summary>Base for every PCSP message. Sealed hierarchy — one record per <see cref="MessageKind"/>.</summary>
public abstract record Message
{
    public abstract MessageKind Kind { get; }
}

/// <summary>
/// First message each side sends: protocol version, self-certifying id, capability flags, and the
/// sender's knowledge frontier. Receiving it drives the local session to <c>Established</c>.
/// </summary>
public sealed record HelloMessage(
    ProtocolVersion Version,
    NodeId NodeId,
    IReadOnlyList<string> Capabilities,
    VectorClock Frontier) : Message
{
    public override MessageKind Kind => MessageKind.Hello;
}

/// <summary>A batch of records the recipient was missing (computed from its Hello frontier).</summary>
public sealed record PushMessage(IReadOnlyList<SyncRecord> Records) : Message
{
    public override MessageKind Kind => MessageKind.Push;
}

/// <summary>The recipient's new frontier after applying a <see cref="PushMessage"/>.</summary>
public sealed record AckMessage(VectorClock Frontier) : Message
{
    public override MessageKind Kind => MessageKind.Ack;
}

/// <summary>Orderly close with a human-readable reason.</summary>
public sealed record ByeMessage(string Reason) : Message
{
    public override MessageKind Kind => MessageKind.Bye;
}

/// <summary>
/// An operator chat line (since 1.1). Carries no sender id on purpose: the sender is whoever the
/// secure channel authenticated — the transport certificate, not the message, names the author,
/// so a line cannot be spoofed. A reconciliation session ignores these.
/// </summary>
public sealed record ChatMessage(string Text, long SentAtUnixMs) : Message
{
    public override MessageKind Kind => MessageKind.Chat;
}
