namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchSyncView(
    bool Accepted, string Status, string Message);
