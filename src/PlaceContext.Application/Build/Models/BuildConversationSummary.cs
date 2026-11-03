using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public sealed record BuildConversationSummary(Guid Id, string Title, DateTimeOffset UpdatedAt, BuildRunStatus Status = BuildRunStatus.Idle);
