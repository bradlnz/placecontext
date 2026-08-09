namespace PlaceContext.Identity.Contracts.Api;

public sealed record CreateRoleRequest(string Name, IReadOnlyList<string> Permissions);
