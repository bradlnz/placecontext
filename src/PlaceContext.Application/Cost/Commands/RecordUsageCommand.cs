using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Record LLM token usage against a project (metadata only — model + token counts).</summary>
public sealed record RecordUsageCommand(Guid ProjectId, string Model, long InputTokens, long OutputTokens, string? Description)
    : ICommand<UsageEntryView>;
