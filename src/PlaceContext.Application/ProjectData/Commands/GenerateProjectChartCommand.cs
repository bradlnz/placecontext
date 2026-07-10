using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Draw a chart over one table of the project's database, synchronously, and return the HTML.
/// Slow on CPU (minutes) — the portal uses the background sweep + stored charts instead; this
/// remains for programmatic/MCP callers that want a one-off chart.
/// </summary>
public sealed record GenerateProjectChartCommand(Guid ProjectId, string TableName, string? Instruction) : ICommand<string>;
