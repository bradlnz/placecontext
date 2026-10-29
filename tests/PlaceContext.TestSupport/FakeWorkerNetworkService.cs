using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeWorkerNetworkService : IWorkerNetworkService
{
    public List<WorkerNetwork> Networks { get; } = [];
    public List<JobNetworkAssignment> Assignments { get; } = [];
    public bool FailAssignment { get; set; }
    public Task DeleteAsync(Guid projectId, Guid networkId, long version, CancellationToken ct = default)
    {
        if (Assignments.Any(item => item.NetworkId == networkId)) throw new InvalidOperationException("Remove job assignments first.");
        Networks.RemoveAll(item => item.ProjectId == projectId && item.Id == networkId && item.Version == version);
        return Task.CompletedTask;
    }
    public Task<WorkerNetworkState> LoadAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult(new WorkerNetworkState(
        Networks.Where(network => network.ProjectId == projectId).ToArray(), Assignments.Where(assignment => Networks.Any(network => network.ProjectId == projectId && network.Id == assignment.NetworkId)).ToArray()));
    public Task SaveAsync(WorkerNetwork network, CancellationToken ct = default)
    {
        WorkerNetworkPolicy.ValidateNetwork(network.Name, network.Cidr, network.Groups);
        Networks.RemoveAll(item => item.Id == network.Id);
        Networks.Add(network with { Version = network.Version + 1 });
        return Task.CompletedTask;
    }
    public Task AssignAsync(Guid projectId, Guid jobId, Guid? networkId, Guid? groupId, CancellationToken ct = default)
    {
        if (FailAssignment) throw new InvalidOperationException("Assignment unavailable");
        Assignments.RemoveAll(item => item.JobId == jobId);
        if (networkId is { } id) Assignments.Add(new(jobId, id, groupId!.Value));
        return Task.CompletedTask;
    }
    public Task<WorkerNetworkPolicy?> ResolveAsync(Guid projectId, Guid jobId, CancellationToken ct = default)
    {
        var assignment = Assignments.FirstOrDefault(item => item.JobId == jobId);
        var network = Networks.FirstOrDefault(item => item.ProjectId == projectId && item.Id == assignment?.NetworkId);
        return Task.FromResult(network is null ? null : new WorkerNetworkPolicy(Guid.NewGuid(), network.Id, network.Cidr,
            network.Groups.Single(group => group.Id == assignment!.GroupId)));
    }
}
