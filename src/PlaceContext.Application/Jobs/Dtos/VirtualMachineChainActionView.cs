using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record VirtualMachineChainActionView(string ImageProfile, string Command, string OutputField,
    int MemoryMb, int Cpus, int TimeoutSeconds, bool AllowNetwork, bool PauseForLogin = false, Guid? LoginProfileId = null)
    : ChainActionView(VirtualMachineChainAction.ActionType, "QEMU environment");
