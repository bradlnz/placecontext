using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Integration;

namespace PlaceContext.Search.Infrastructure.OpenSearch;

public sealed class OpenSearchConnectionResolver : IOpenSearchConnectionResolver
{
    private readonly OpenSearchOptions _options;
    private readonly ISearchSecretProvider? _secrets;

    public OpenSearchConnectionResolver(
        IOptions<OpenSearchOptions> options,
        ISearchSecretProvider? secrets = null)
        => (_options, _secrets) = (options.Value, secrets);

    public async Task<OpenSearchConnection?> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_secrets is not null)
        {
            try
            {
                var names = new[]
                {
                    OpenSearchEnvironmentVariables.Endpoint,
                    "OPENSEARCH_ENDPOINT",
                    OpenSearchEnvironmentVariables.Username,
                    OpenSearchEnvironmentVariables.Password,
                    OpenSearchEnvironmentVariables.Index,
                };
                foreach (var pair in await _secrets.GetSecretsAsync(projectId, names, ct))
                    values[pair.Key] = pair.Value;
            }
            catch { /* Vault unavailability must not disable service-level OpenSearch settings. */ }
        }

        var endpoint = Value(values, OpenSearchEnvironmentVariables.Endpoint, "OPENSEARCH_ENDPOINT") ?? _options.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        if (!Uri.TryCreate(endpoint.TrimEnd('/'), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("OPENSEARCH_URL must be an absolute HTTP or HTTPS URL.");

        return new OpenSearchConnection(
            uri.ToString().TrimEnd('/'),
            Value(values, OpenSearchEnvironmentVariables.Username) ?? _options.Username,
            Value(values, OpenSearchEnvironmentVariables.Password) ?? _options.Password,
            Value(values, OpenSearchEnvironmentVariables.Index) ?? _options.DefaultIndexPattern);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var connection = await ResolveAsync(projectId, ct);
        if (connection is null) return new Dictionary<string, string>();
        var env = new Dictionary<string, string>
        {
            [OpenSearchEnvironmentVariables.Endpoint] = connection.Endpoint,
            [OpenSearchEnvironmentVariables.Index] = connection.DefaultIndexPattern,
        };
        if (!string.IsNullOrWhiteSpace(connection.Username))
            env[OpenSearchEnvironmentVariables.Username] = connection.Username;
        if (!string.IsNullOrWhiteSpace(connection.Password))
            env[OpenSearchEnvironmentVariables.Password] = connection.Password;
        return env;
    }

    private static string? Value(
        IReadOnlyDictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }
}
