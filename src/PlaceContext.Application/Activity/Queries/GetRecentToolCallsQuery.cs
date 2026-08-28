using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;
public sealed record GetRecentToolCallsQuery(int Take = 100) : IQuery<IReadOnlyList<ToolCallView>>;
