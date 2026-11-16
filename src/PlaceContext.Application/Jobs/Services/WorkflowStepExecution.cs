using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public static class WorkflowStepExecution
{
    public static async Task<T> RunAsync<T>(StepExecutionPolicy? policy, Func<CancellationToken, Task<T>> execute,
        CancellationToken ct, int retriesUsed = 0, Func<int, string, Task>? retrying = null, Func<T, bool>? failed = null,
        Func<Task>? checkCancellation = null)
    {
        if (policy is null) return await execute(ct);
        for (var retry = retriesUsed; ; retry++)
        {
            ct.ThrowIfCancellationRequested();
            if (checkCancellation is not null) await checkCancellation();
            string reason;
            var delay = TimeSpan.FromSeconds(policy.RetryDelaySeconds);
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (policy.TimeoutSeconds is { } seconds) attempt.CancelAfter(TimeSpan.FromSeconds(seconds));
                try
                {
                    var result = await execute(attempt.Token);
                    attempt.Token.ThrowIfCancellationRequested();
                    if (failed?.Invoke(result) != true || retry >= policy.MaxRetries) return result;
                    reason = "Step reported a failure.";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is not (UnauthorizedAccessException or ArgumentException)
                    && (ex is not OperationCanceledException || attempt.IsCancellationRequested))
                {
                    var failure = attempt.IsCancellationRequested
                        ? new TimeoutException($"Step exceeded its {policy.TimeoutSeconds}s execution limit.", ex) : ex;
                    if (retry >= policy.MaxRetries) throw failure;
                    reason = failure.Message;
                    if (ex is PlaceContext.Application.Build.OutcomeReviewThrottledException throttled && throttled.RetryAfter > delay)
                        delay = throttled.RetryAfter;
                }
            }
            if (checkCancellation is not null) await checkCancellation();
            if (retrying is not null) await retrying(retry + 1, reason);
            while (delay > TimeSpan.Zero)
            {
                var slice = delay > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                await Task.Delay(slice, ct);
                delay -= slice;
                if (checkCancellation is not null) await checkCancellation();
            }
        }
    }
}
