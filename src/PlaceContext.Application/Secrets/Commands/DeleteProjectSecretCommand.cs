using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Delete a vault secret. To rotate a secret, delete then add a new one.</summary>
public sealed record DeleteProjectSecretCommand(Guid ProjectId, string Name) : ICommand<bool>;
