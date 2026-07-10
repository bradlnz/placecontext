using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetEffectiveRequirementsHandler : IQueryHandler<GetEffectiveRequirementsQuery, EffectiveRequirementsView>
{
    private readonly IRequirementsRepository _requirements;
    public GetEffectiveRequirementsHandler(IRequirementsRepository requirements) => _requirements = requirements;

    public async Task<EffectiveRequirementsView> HandleAsync(GetEffectiveRequirementsQuery query, CancellationToken ct = default)
    {
        var global = await _requirements.GetGlobalAsync(ct);
        var project = await _requirements.GetForProjectAsync(ProjectId.From(query.ProjectId), ct);

        var sections = new List<string>();
        if (global is { IsEmpty: false }) sections.Add("# Global requirements\n\n" + global.Markdown.Trim());
        if (project is { IsEmpty: false }) sections.Add("# Project requirements\n\n" + project.Markdown.Trim());

        var merged = string.Join("\n\n", sections);
        return new EffectiveRequirementsView(query.ProjectId, merged, merged.Length == 0);
    }
}
