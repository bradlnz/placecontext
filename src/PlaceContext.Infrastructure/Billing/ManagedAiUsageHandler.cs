using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>Reserve before dispatch; failed provider attempts count, avoiding an unmetered retry loop.</summary>
public sealed class ManagedAiUsageHandler(IServiceScopeFactory scopes) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method == HttpMethod.Post)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DbRunAllowance>().ReserveAiRequestAsync(ct);
        }
        return await base.SendAsync(request, ct);
    }
}
