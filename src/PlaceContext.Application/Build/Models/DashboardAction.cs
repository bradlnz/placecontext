namespace PlaceContext.Application.Build;

public sealed record DashboardAction(string Title, string Prompt)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Description { get; init; }
}
