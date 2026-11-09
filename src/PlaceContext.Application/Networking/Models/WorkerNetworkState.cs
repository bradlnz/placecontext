namespace PlaceContext.Application.Networking;

public sealed record WorkerNetworkState(IReadOnlyList<WorkerNetwork> Networks, IReadOnlyList<JobNetworkAssignment> Assignments);
