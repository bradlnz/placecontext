namespace PlaceContext.Application.Dtos;

public sealed record SearchSyncView(
    bool Accepted, string Status, string Message);
