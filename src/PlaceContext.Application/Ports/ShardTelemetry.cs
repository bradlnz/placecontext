namespace PlaceContext.Application.Ports;

/// <summary>One map shard captured from the OTel <c>job.shard</c> activity.</summary>
public sealed record ShardTelemetry(int Index, string? Outcome, int? ExitCode, double? DurationMs);
