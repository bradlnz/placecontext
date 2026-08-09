using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Infrastructure.Integration;

public sealed class HttpDataSearchClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IDataSearchClient
{
    public async Task<IReadOnlyList<DataSearchIndexSummary>> ListIndicesAsync(
        Guid projectId, CancellationToken ct = default)
        => await SendAsync<IReadOnlyList<DataSearchIndexSummary>>(
            HttpMethod.Get, projectId, "indices", null, ct) ?? [];

    public async Task<ProjectQueryResult> QueryAsync(
        Guid projectId, string sql, CancellationToken ct = default)
        => await SendAsync<ProjectQueryResult>(
            HttpMethod.Post, projectId, "sql", new DataSearchSqlRequest(sql), ct)
            ?? new ProjectQueryResult([], [], 0, false);

    public async Task ReplaceIndexAsync(
        Guid projectId,
        string indexName,
        IReadOnlyList<DataSearchMappingField> mappingFields,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string?>> rows,
        IReadOnlyList<string> jsonColumnNames,
        CancellationToken ct = default)
        => _ = await SendAsync<object>(HttpMethod.Put, projectId, "indices", new ReplaceDataSearchIndexRequest(
            indexName, mappingFields, columnNames, rows, jsonColumnNames), ct);

    private async Task<T?> SendAsync<T>(
        HttpMethod method, Guid projectId, string path, object? body, CancellationToken ct)
    {
        var origin = configuration["PlaceContext:Data:Search:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Search"]
            ?? throw new InvalidOperationException("Configure the Search service destination for Data.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            method,
            new Uri(new Uri(baseAddress), $"api/search/internal/projects/{projectId}/{path}"));
        request.Headers.Add("X-Api-Key", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
