namespace PlaceContext.Jobs.Integration;

public sealed record JobCrmCustomer(
    Guid Id,
    string Name,
    string? Company,
    string? Email,
    string? Phone);
