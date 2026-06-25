using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetGlobalRequirementsHandler : IQueryHandler<GetGlobalRequirementsQuery, RequirementsView>
{
    private readonly IRequirementsRepository _requirements;
    public GetGlobalRequirementsHandler(IRequirementsRepository requirements) => _requirements = requirements;

    public async Task<RequirementsView> HandleAsync(GetGlobalRequirementsQuery query, CancellationToken ct = default)
    {
        var doc = await _requirements.GetGlobalAsync(ct);
        return doc is null ? RequirementsView.EmptyGlobal() : ViewMapper.ToView(doc);
    }
}
