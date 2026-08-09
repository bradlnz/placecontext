using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

public sealed record OpenSearchConnection(
    string Endpoint,
    string? Username,
    string? Password,
    string DefaultIndexPattern);
