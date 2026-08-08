namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record CreateRoleRequest(string Name, IReadOnlyList<string> Permissions);
