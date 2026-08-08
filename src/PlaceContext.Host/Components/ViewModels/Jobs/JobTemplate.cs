using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>A pre-built starting point for a new job. Templates are pure presentation-layer
/// defaults — they pre-fill the generic job editor with sample code, env vars, parameters and
/// a list of vault credentials the integration needs.</summary>
public sealed record JobTemplate(
    string Id,
    string Name,
    string Category,
    string Description,
    string Icon,
    string MapSourceKind,
    string? MapRuntimeId,
    string? MapEntrypoint,
    string MapSource,
    string MapEnvRaw,
    string InputPayloadsRaw,
    IReadOnlyList<JobParameterDto> Parameters,
    JobReturnType ReturnType,
    bool AllowNetworkEgress,
    IReadOnlyList<JobCredentialRequirement> RequiredCredentials)
{
    public string MapImage => MapSourceKind == "image" ? MapSource : "";
}
