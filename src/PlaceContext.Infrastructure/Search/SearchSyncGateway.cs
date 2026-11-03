using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Search;
namespace PlaceContext.Infrastructure.Search;

public sealed class SearchSyncGateway : ISearchSyncGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly SearchOptions _options;

    public SearchSyncGateway(
        IHttpClientFactory httpFactory,
        IOptions<SearchOptions> options)
        => (_httpFactory, _options) = (httpFactory, options.Value);

    public async Task<SearchSyncView> TriggerAsync(CancellationToken ct = default)
    {
        if (!Uri.TryCreate(_options.SyncEndpoint, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(_options.SyncToken))
        {
            throw new InvalidOperationException("Manual OpenSearch sync is not configured.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SyncToken);
        using var response = await _httpFactory.CreateClient("opensearch-sync")
            .SendAsync(request, ct);
        if (!response.IsSuccessStatusCode
            && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                        $"The OpenSearch collector rejected the sync request ({(int)response.StatusCode}).");
        }
        var result = await response.Content.ReadFromJsonAsync<SearchSyncView>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException(
            "The OpenSearch collector returned an invalid sync response.");
    }
}