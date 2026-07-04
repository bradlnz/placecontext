namespace PlaceContext.Sync;

/// <summary>The lifecycle of a <see cref="SyncSession"/> (docs/SYNC-PROTOCOL.md §6).</summary>
public enum SessionState
{
    New,
    HelloSent,
    Established,
    Closed,
}

/// <summary>
/// The PCSP reconciliation state machine — pure and transport-free. You feed it inbound
/// <see cref="Message"/>s and it returns the outbound ones to write, mutating an <see cref="ISyncStore"/>
/// as records flow. Symmetric: both peers run an identical session with no client/server role.
///
/// Drive it: call <see cref="Start"/> once (returns the opening Hello), then pass every inbound
/// message to <see cref="Receive"/> and write whatever it returns. A single ordered stream guarantees
/// a peer's Hello arrives before any Push it sends.
/// </summary>
public sealed class SyncSession
{
    private readonly NodeId _localId;
    private readonly ProtocolVersion _localVersion;
    private readonly IReadOnlyList<string> _capabilities;
    private readonly ISyncStore _store;

    private VectorClock? _peerFrontier;

    public SyncSession(
        NodeId localId,
        ISyncStore store,
        ProtocolVersion? version = null,
        IReadOnlyList<string>? capabilities = null)
    {
        _localId = localId ?? throw new ArgumentNullException(nameof(localId));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _localVersion = version ?? ProtocolVersion.Current;
        _capabilities = capabilities ?? Array.Empty<string>();
    }

    public SessionState State { get; private set; } = SessionState.New;
    public NodeId? PeerId { get; private set; }
    public ProtocolVersion? AgreedVersion { get; private set; }

    /// <summary>
    /// True once this side has both pushed its delta and learned (via the peer's Ack) that the peer
    /// holds everything this node holds — i.e. it is safe to <see cref="Close"/>.
    /// </summary>
    public bool IsConverged =>
        State == SessionState.Established
        && _peerFrontier is not null
        && _peerFrontier.Dominates(_store.Frontier())
        && _store.Frontier().Dominates(_peerFrontier);

    /// <summary>Emit the opening Hello. Call exactly once, before any <see cref="Receive"/>.</summary>
    public IReadOnlyList<Message> Start()
    {
        if (State != SessionState.New)
            throw new InvalidOperationException($"Start() valid only in New (was {State}).");

        State = SessionState.HelloSent;
        return new[] { (Message)new HelloMessage(_localVersion, _localId, _capabilities, _store.Frontier()) };
    }

    /// <summary>Process one inbound message; returns the messages to write back (possibly empty).</summary>
    public IReadOnlyList<Message> Receive(Message inbound)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        if (State == SessionState.Closed)
            return Array.Empty<Message>(); // ignore anything after close

        return inbound switch
        {
            HelloMessage hello => OnHello(hello),
            PushMessage push => OnPush(push),
            AckMessage ack => OnAck(ack),
            ByeMessage => OnBye(),
            _ => throw new InvalidOperationException($"Unhandled message {inbound.Kind}."),
        };
    }

    /// <summary>Close the session, emitting a Bye with <paramref name="reason"/>.</summary>
    public IReadOnlyList<Message> Close(string reason = "closing")
    {
        if (State == SessionState.Closed) return Array.Empty<Message>();
        State = SessionState.Closed;
        return new[] { (Message)new ByeMessage(reason) };
    }

    private IReadOnlyList<Message> OnHello(HelloMessage hello)
    {
        if (State != SessionState.HelloSent)
            throw new InvalidOperationException($"Hello valid only in HelloSent (was {State}).");

        if (!_localVersion.TryNegotiate(hello.Version, out var agreed))
        {
            State = SessionState.Closed;
            return new[] { (Message)new ByeMessage($"version: incompatible major (local {_localVersion}, peer {hello.Version})") };
        }

        AgreedVersion = agreed;
        PeerId = hello.NodeId;
        _peerFrontier = hello.Frontier;
        State = SessionState.Established;

        // Push exactly the records the peer is missing (may be empty — an explicit "nothing for you").
        var delta = _store.RecordsSince(hello.Frontier);
        return new[] { (Message)new PushMessage(delta) };
    }

    private IReadOnlyList<Message> OnPush(PushMessage push)
    {
        if (State != SessionState.Established)
            throw new InvalidOperationException($"Push valid only in Established (was {State}).");

        foreach (var record in push.Records)
            _store.Apply(record); // idempotent by (origin, sequence)

        return new[] { (Message)new AckMessage(_store.Frontier()) };
    }

    private IReadOnlyList<Message> OnAck(AckMessage ack)
    {
        if (State != SessionState.Established)
            throw new InvalidOperationException($"Ack valid only in Established (was {State}).");

        // The peer now holds at least this much; merge it into what we know of them.
        _peerFrontier = _peerFrontier is null ? ack.Frontier : _peerFrontier.Merge(ack.Frontier);
        return Array.Empty<Message>();
    }

    private IReadOnlyList<Message> OnBye()
    {
        State = SessionState.Closed;
        return Array.Empty<Message>();
    }
}
