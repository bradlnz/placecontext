namespace PlaceContext.Application.Workload.VirtualMachines;

public sealed record VirtualMachineEnvironmentResult(int ExitCode, string Stdout, string Stderr, bool OutputTruncated = false,
    int AgentSteps = 0, IReadOnlyList<VirtualMachineAgentActivity>? Activity = null);
