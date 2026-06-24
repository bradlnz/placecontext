using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetGlobalRequirementsHandler : IQueryHandler<GetGlobalRequirementsQuery, CodeRequirementsView>
{
    private readonly ICodeRequirementsRepository _requirements;
    public GetGlobalRequirementsHandler(ICodeRequirementsRepository requirements) => _requirements = requirements;

    public async Task<CodeRequirementsView> HandleAsync(GetGlobalRequirementsQuery query, CancellationToken ct = default)
    {
        var doc = await _requirements.GetGlobalAsync(ct);
        return doc is null ? CodeRequirementsView.EmptyGlobal() : ViewMapper.ToView(doc);
    }
}
