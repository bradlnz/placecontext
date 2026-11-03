namespace PlaceContext.Application.Jobs;

public interface IJobDataTokenService
{
    Task<string> CreateAsync(Guid runId, CancellationToken ct = default);
    Task<JobDataGrant?> ValidateAsync(string token, CancellationToken ct = default);
}
