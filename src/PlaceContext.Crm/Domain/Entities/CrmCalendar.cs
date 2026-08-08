namespace PlaceContext.Domain.Entities;

public sealed class CrmCalendar
{
    private CrmCalendar(Guid id, Guid projectId, string name, string color, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => (Id, ProjectId, Name, Color, CreatedAt, UpdatedAt) = (id, projectId, name, color, createdAt, updatedAt);
    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string Color { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CrmCalendar Create(Guid projectId, string name, string color, DateTimeOffset now)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        Validate(name, color);
        return new(Guid.NewGuid(), projectId, name.Trim(), color, now, now);
    }
    public static CrmCalendar Rehydrate(Guid id, Guid projectId, string name, string color, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, color, createdAt, updatedAt);
    public void Update(string name, string color, DateTimeOffset now) { Validate(name, color); Name = name.Trim(); Color = color; UpdatedAt = now; }
    private static void Validate(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Calendar name is required.", nameof(name));
        if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit)) throw new ArgumentException("Choose a valid calendar color.", nameof(color));
    }
}
