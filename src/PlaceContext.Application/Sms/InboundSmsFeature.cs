using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>One inbound SMS, decrypted for an authorized reader.</summary>
public sealed record InboundSmsView(
    Guid Id, string From, string To, string Body, string Provider, Guid? ProjectId, DateTimeOffset ReceivedAt);

/// <summary>
/// Accept one inbound SMS from the gateway webhook. The sender and body are encrypted before
/// anything touches the store; the emitted <c>sms.received</c> event (which can fire job triggers)
/// carries only routing metadata and a masked sender — never the message text.
/// </summary>
public sealed record ReceiveInboundSmsCommand(
    string From, string To, string Body, string Provider, string? ExternalId, Guid? ProjectId)
    : ICommand<InboundSmsView>;

/// <summary>Recent inbound SMS, decrypted for display in the portal.</summary>
public sealed record ListInboundSmsQuery(int Take = 50) : IQuery<IReadOnlyList<InboundSmsView>>;

public sealed class ReceiveInboundSmsHandler : ICommandHandler<ReceiveInboundSmsCommand, InboundSmsView>
{
    private readonly IInboundSmsRepository _messages;
    private readonly ISecretProtector _protector;
    private readonly IDispatcher _dispatcher;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public ReceiveInboundSmsHandler(IInboundSmsRepository messages, ISecretProtector protector,
        IDispatcher dispatcher, IUnitOfWork uow, IClock clock)
    {
        _messages = messages;
        _protector = protector;
        _dispatcher = dispatcher;
        _uow = uow;
        _clock = clock;
    }

    public async Task<InboundSmsView> HandleAsync(ReceiveInboundSmsCommand c, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(c.From)) throw new ArgumentException("Sender is required.", nameof(c));
        if (string.IsNullOrWhiteSpace(c.Body)) throw new ArgumentException("Message body is required.", nameof(c));

        // Delivery gateways retry webhooks; the provider's message id makes the retry a no-op.
        if (!string.IsNullOrWhiteSpace(c.ExternalId)
            && await _messages.ExistsAsync(c.Provider, c.ExternalId!, ct))
            return ToView(c, Guid.Empty);

        var message = SmsMessage.Receive(
            c.ProjectId,
            _protector.Protect(c.From.Trim()),
            c.To.Trim(),
            _protector.Protect(c.Body),
            c.Provider, c.ExternalId, _clock.UtcNow);
        await _messages.AddAsync(message, ct);
        await _uow.SaveChangesAsync(ct);

        // The event fans out to triggers/jobs. Metadata only: a masked sender is enough to route on,
        // and the body must never exist outside the encrypted row (jobs can query it via a future
        // scoped read if a use case demands it).
        var payload = JsonSerializer.Serialize(new
        {
            from = Mask(c.From.Trim()),
            to = c.To.Trim(),
            provider = message.Provider,
            messageId = message.Id,
            receivedAt = message.ReceivedAt,
        });
        await _dispatcher.Send(new EmitEventCommand("sms.received", c.ProjectId, payload), ct);

        return new InboundSmsView(message.Id, c.From.Trim(), c.To.Trim(), c.Body,
            message.Provider, c.ProjectId, message.ReceivedAt);
    }

    // Dedupe path: acknowledge the retry without re-storing or re-firing anything.
    private static InboundSmsView ToView(ReceiveInboundSmsCommand c, Guid id)
        => new(id, c.From.Trim(), c.To.Trim(), c.Body, c.Provider, c.ProjectId, DateTimeOffset.MinValue);

    /// <summary>"+15551234567" → "…4567": routable, but not the number.</summary>
    internal static string Mask(string number)
        => number.Length <= 4 ? "…" : "…" + number[^4..];
}

public sealed class ListInboundSmsHandler : IQueryHandler<ListInboundSmsQuery, IReadOnlyList<InboundSmsView>>
{
    private readonly IInboundSmsRepository _messages;
    private readonly ISecretProtector _protector;

    public ListInboundSmsHandler(IInboundSmsRepository messages, ISecretProtector protector)
    {
        _messages = messages;
        _protector = protector;
    }

    public async Task<IReadOnlyList<InboundSmsView>> HandleAsync(ListInboundSmsQuery q, CancellationToken ct = default)
        => (await _messages.ListRecentAsync(Math.Clamp(q.Take, 1, 200), ct))
            .Select(m => new InboundSmsView(
                m.Id,
                SafeUnprotect(m.FromProtected), m.To, SafeUnprotect(m.BodyProtected),
                m.Provider, m.ProjectId, m.ReceivedAt))
            .ToList();

    // A rotated/lost key must degrade to an unreadable row, not a broken page.
    private string SafeUnprotect(string ciphertext)
    {
        try { return _protector.Unprotect(ciphertext); }
        catch { return "(undecryptable)"; }
    }
}
