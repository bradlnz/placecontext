using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Add a new vault secret to a project (value encrypted at rest). Fails if the name exists.</summary>
public sealed record AddProjectSecretCommand(Guid ProjectId, string Name, string Value) : ICommand<ProjectSecretView>;
