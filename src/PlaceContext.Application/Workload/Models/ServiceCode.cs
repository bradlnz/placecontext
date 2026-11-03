using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Workload;

public sealed record ServiceCode(string RuntimeId, string Entrypoint, IReadOnlyList<CodeFileDto> Files, bool AllowNetworkEgress = false);
