using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record QemuChainActionView(string ImageProfile, string Command, string OutputField,
    int MemoryMb, int Cpus, int TimeoutSeconds, bool AllowNetwork)
    : ChainActionView(QemuChainAction.ActionType, "QEMU environment");
