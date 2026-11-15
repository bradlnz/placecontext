using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ContainerRunChainActionView(string Image, string ImageField, string OutputField, IReadOnlyList<string> Command,
    int? Port, int MemoryMb, int Cpus, int TimeoutSeconds, bool AllowNetwork)
    : ChainActionView(ContainerRunChainAction.ActionType, "Run container");
