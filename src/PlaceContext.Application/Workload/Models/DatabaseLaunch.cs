namespace PlaceContext.Application.Workload;

public sealed record DatabaseLaunch(string Name, string Engine, int MemoryMb = 512, decimal Cpus = 1, Guid? ServiceId = null);
