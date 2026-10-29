using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public sealed record QemuAgentActivity(string Tool, string Description, int? ExitCode);
