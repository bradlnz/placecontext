namespace PlaceContext.Application.Ports;
public sealed record ServiceInstance(Guid Id, string Name, string Image, string Status, string? Health, int? HostPort, DateTimeOffset CreatedAt, IReadOnlyList<Guid>? DatabaseIds = null, bool RequireSso = false, string? Namespace = null);
