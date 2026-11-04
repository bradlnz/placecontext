using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Workload;

/// <summary>Throttle every TypeSafe call, including retries, through the shared organization budget.</summary>
public sealed class TypeSafeRateLimitHandler(ITypeSafeRateBudget budget, ILogger<TypeSafeRateLimitHandler> log) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        var inputBytes = body.Length;
        if (body.Length > 0)
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String)
            {
                // Transport escaping of JSON text is not additional model input.
                inputBytes += Encoding.UTF8.GetByteCount(state.GetString()!) + 2 - Encoding.UTF8.GetByteCount(state.GetRawText());
            }
        }
        // shortcut: UTF-8 input bytes plus overhead reserve tokens; use a provider tokenizer for better utilization.
        var reservedTokens = checked(inputBytes + 1024);
        for (var attempt = 0; ; attempt++)
        {
            await budget.AcquireAsync(reservedTokens, ct);
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
                Content = new ByteArrayContent(body)
            };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
                foreach (var header in request.Content.Headers) copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var response = await base.SendAsync(copy, ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode != 529)
                return response;
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
            var retryAfter = response.Headers.RetryAfter;
            var requested = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.Zero;
            if (requested > backoff) backoff = requested;
            try
            {
                await budget.CooldownAsync(backoff, ct);
                log.LogWarning("TypeSafe HTTP {Status}: shared cooldown {Seconds}s, attempt {Attempt}.", (int)response.StatusCode, backoff.TotalSeconds, attempt + 1);
            }
            catch
            {
                response.Dispose();
                throw;
            }
            if (attempt >= 3) return response;
            response.Dispose();
            // The database cooldown also holds other requests, not only this retry.
        }
    }
}
