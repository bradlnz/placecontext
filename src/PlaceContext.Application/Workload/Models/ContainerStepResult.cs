namespace PlaceContext.Application.Workload;

public sealed record ContainerStepResult(int ExitCode, string Stdout, string Stderr, string? Image = null, string? Url = null, string? QemuUrl = null);
