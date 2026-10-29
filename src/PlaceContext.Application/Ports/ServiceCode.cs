using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

public sealed record ServiceCode(string RuntimeId, string Entrypoint, IReadOnlyList<CodeFileDto> Files, bool AllowNetworkEgress = false);
