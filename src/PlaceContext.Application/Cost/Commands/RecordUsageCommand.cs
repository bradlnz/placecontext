using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Record LLM token usage against a project (metadata only — model + token counts).</summary>
public sealed record RecordUsageCommand(Guid ProjectId, string Model, long InputTokens, long OutputTokens, string? Description)
    : ICommand<UsageEntryView>;
