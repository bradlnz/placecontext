using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Recent inbound SMS, decrypted for display in the portal.</summary>
public sealed record ListInboundSmsQuery(int Take = 50) : IQuery<IReadOnlyList<InboundSmsView>>;
