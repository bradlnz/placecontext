namespace PlaceContext.Jobs.Integration;

public interface IJobCrmClient
{
    Task<JobCrmCustomer?> GetCustomerAsync(Guid id, CancellationToken ct = default);
    Task NotifyChainCompletedAsync(
        JobCrmChainCompletion completion,
        CancellationToken ct = default);
}
