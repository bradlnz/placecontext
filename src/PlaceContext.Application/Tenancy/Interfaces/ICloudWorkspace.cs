namespace PlaceContext.Application.Tenancy;

public interface ICloudWorkspace
{
    Task<CloudWorkspaceStatus?> GetAsync(CancellationToken ct = default);
    Task RetryAsync(CancellationToken ct = default);
}

public sealed record CloudWorkspaceStatus(string State, string Plan, int Attempts, DateTimeOffset? RetryAt);
