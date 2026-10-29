using System.Text.Json;

namespace PlaceContext.Application.Ports;

public sealed record AppQueueResult(JobChainSubmission Receipt, string? Output);
public interface IAppQueueGateway
{
    Task<JobChainSubmission> SubmitAsync(string credential, Guid queueId, JsonElement payload, string idempotencyKey, CancellationToken ct);
    Task<AppQueueResult?> GetAsync(string credential, Guid queueId, Guid trackingId, CancellationToken ct);
}
