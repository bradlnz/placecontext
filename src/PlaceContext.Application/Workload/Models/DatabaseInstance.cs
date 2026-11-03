namespace PlaceContext.Application.Workload;

public sealed record DatabaseInstance(Guid Id, string Name, string Engine, string Status, string? Health,
    string Host, int Port, string Database, string Username, string Volume, DateTimeOffset CreatedAt, Guid? ServiceId = null, string? Namespace = null);
