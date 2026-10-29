namespace PlaceContext.Application.Ports;

public sealed record ServiceLaunch(string Name, string Image, int? ContainerPort, int MemoryMb = 256, decimal Cpus = 1, string Environment = "", IReadOnlyList<Guid>? DatabaseIds = null, bool RequireSso = false, IReadOnlyList<Guid>? QueueIds = null, ServiceCode? Code = null);
