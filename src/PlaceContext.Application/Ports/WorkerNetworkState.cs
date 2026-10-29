using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;
public sealed record WorkerNetworkState(IReadOnlyList<WorkerNetwork> Networks, IReadOnlyList<JobNetworkAssignment> Assignments);
