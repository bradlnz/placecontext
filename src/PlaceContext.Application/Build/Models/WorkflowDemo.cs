namespace PlaceContext.Application.Build;

public sealed record WorkflowDemo(string Title, string Status, string Outcome, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, IReadOnlyList<WorkflowDemoFrame> Frames);
