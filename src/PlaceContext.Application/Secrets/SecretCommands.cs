using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Add a new vault secret to a project (value encrypted at rest). Fails if the name exists.</summary>
public sealed record AddProjectSecretCommand(Guid ProjectId, string Name, string Value) : ICommand<ProjectSecretView>;

/// <summary>Delete a vault secret. To rotate a secret, delete then add a new one.</summary>
public sealed record DeleteProjectSecretCommand(Guid ProjectId, string Name) : ICommand<bool>;

/// <summary>List a project's secret names (never values).</summary>
public sealed record ListProjectSecretsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectSecretView>>;
