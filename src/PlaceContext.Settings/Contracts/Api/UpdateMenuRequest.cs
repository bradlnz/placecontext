namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateMenuRequest(IReadOnlyList<UpdateMenuItemRequest> Workspace);
