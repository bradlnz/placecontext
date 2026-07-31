using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.OpenSearch;

public sealed class OpenSearchConnectionResolver : IOpenSearchConnectionResolver
{
    public const string EndpointVariable = "OPENSEARCH_URL";
    public const string UsernameVariable = "OPENSEARCH_USERNAME";
    public const string PasswordVariable = "OPENSEARCH_PASSWORD";
    public const string IndexVariable = "OPENSEARCH_INDEX";

    private readonly OpenSearchOptions _options;
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;

    public OpenSearchConnectionResolver(
        IOptions<OpenSearchOptions> options,
        IProjectSecretRepository secrets,
        ISecretProtector protector)
        => (_options, _secrets, _protector) = (options.Value, secrets, protector);

    public async Task<OpenSearchConnection?> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, cipher) in await _secrets.GetCiphersAsync(projectId, ct))
        {
            try { values[name] = _protector.Unprotect(cipher); }
            catch { /* A damaged unrelated Vault entry must not disable Data Search. */ }
        }

        var endpoint = Value(values, EndpointVariable, "OPENSEARCH_ENDPOINT") ?? _options.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        if (!Uri.TryCreate(endpoint.TrimEnd('/'), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("OPENSEARCH_URL must be an absolute HTTP or HTTPS URL.");

        return new OpenSearchConnection(
            uri.ToString().TrimEnd('/'),
            Value(values, UsernameVariable) ?? _options.Username,
            Value(values, PasswordVariable) ?? _options.Password,
            Value(values, IndexVariable) ?? _options.DefaultIndexPattern);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var connection = await ResolveAsync(projectId, ct);
        if (connection is null) return new Dictionary<string, string>();
        var env = new Dictionary<string, string>
        {
            [EndpointVariable] = connection.Endpoint,
            [IndexVariable] = connection.DefaultIndexPattern,
        };
        if (!string.IsNullOrWhiteSpace(connection.Username))
            env[UsernameVariable] = connection.Username;
        if (!string.IsNullOrWhiteSpace(connection.Password))
            env[PasswordVariable] = connection.Password;
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
