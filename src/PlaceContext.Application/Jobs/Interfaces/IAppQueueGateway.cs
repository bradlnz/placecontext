using System.Text.Json;

namespace PlaceContext.Application.Jobs;

public interface IAppQueueGateway
{
    Task<JobChainSubmission> SubmitAsync(string credential, Guid queueId, JsonElement payload, string idempotencyKey, CancellationToken ct);
    Task<AppQueueResult?> GetAsync(string credential, Guid queueId, Guid trackingId, CancellationToken ct);
}
