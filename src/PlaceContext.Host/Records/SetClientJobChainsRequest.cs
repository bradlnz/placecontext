namespace PlaceContext.Host.Controllers;

public sealed record SetClientJobChainsRequest(IReadOnlyList<Guid> ChainIds);
