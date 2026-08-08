using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>List a project's secret names (never values).</summary>
public sealed record ListProjectSecretsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectSecretView>>;
