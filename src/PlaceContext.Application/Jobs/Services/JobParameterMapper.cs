using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Maps job-parameter transport DTOs to domain value objects.</summary>
internal static class JobParameterMapper
{
    public static IReadOnlyList<JobParameter> ToDomain(IReadOnlyList<JobParameterDto>? parameters)
        => parameters is null
            ? Array.Empty<JobParameter>()
            : parameters.Select(p => new JobParameter(p.Name, p.Label, p.Required, p.Type, p.Options)).ToList();
}
