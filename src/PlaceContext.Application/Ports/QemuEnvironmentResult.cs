using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;
public sealed record QemuEnvironmentResult(int ExitCode, string Stdout, string Stderr, bool OutputTruncated = false,
    int AgentSteps = 0, IReadOnlyList<QemuAgentActivity>? Activity = null);
