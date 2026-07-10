using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

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
