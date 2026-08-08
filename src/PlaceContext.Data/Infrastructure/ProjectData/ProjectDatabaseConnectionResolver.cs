using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Data.Infrastructure.ProjectData;

/// <summary>
/// Resolves which Postgres a project's data lives in. Defaults to the shared cluster database
/// (<c>PlaceContext:ConnectionString</c>); a per-project external database overrides it via Vault
/// secrets (the same Vault mechanism the OpenSearch resolver uses). The connection string is
/// composed server-side so credentials never appear in UI read models.
/// </summary>
public sealed class ProjectDatabaseConnectionResolver : IProjectDatabaseConnectionResolver
{
    private const string DefaultPort = "5432";
    private const string DefaultSslMode = "Prefer";

    private readonly PlaceContextOptions _options;
    private readonly IProjectSecretRepository? _secrets;
    private readonly ISecretProtector? _protector;

    public ProjectDatabaseConnectionResolver(
        IOptions<PlaceContextOptions> options,
        IProjectSecretRepository? secrets = null,
        ISecretProtector? protector = null)
        => (_options, _secrets, _protector) = (options.Value, secrets, protector);

    public async Task<ProjectDatabaseConnection> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_secrets is not null && _protector is not null)
        {
            foreach (var (name, cipher) in await _secrets.GetCiphersAsync(projectId, ct))
            {
                try { values[name] = _protector.Unprotect(cipher); }
                catch { /* A damaged unrelated Vault entry must not disable project data. */ }
            }
        }

        var host = Value(values, ProjectDatabaseEnvironmentVariables.Host);
        if (string.IsNullOrWhiteSpace(host))
            return new ProjectDatabaseConnection(_options.ConnectionString, IsExternal: false);

        var user = Value(values, ProjectDatabaseEnvironmentVariables.Username);
        var password = Value(values, ProjectDatabaseEnvironmentVariables.Password);
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException(
                $"'{ProjectDatabaseEnvironmentVariables.Username}' is required when an external database is configured.");
        if (password is null)
            throw new InvalidOperationException(
                $"'{ProjectDatabaseEnvironmentVariables.Password}' is required when an external database is configured.");

        var port = Value(values, ProjectDatabaseEnvironmentVariables.Port) ?? DefaultPort;
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new InvalidOperationException($"'{ProjectDatabaseEnvironmentVariables.Port}' must be a number.");

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.Parse(port, CultureInfo.InvariantCulture),
            Database = Value(values, ProjectDatabaseEnvironmentVariables.Name) ?? user,
            Username = user,
            Password = password,
            SslMode = NpgsqlSslModeFrom(Value(values, ProjectDatabaseEnvironmentVariables.SslMode) ?? DefaultSslMode),
        }.ConnectionString;

        return new ProjectDatabaseConnection(connectionString, IsExternal: true);
    }

    private static string? Value(
        IReadOnlyDictionary<string, string> values, params string[] names)
    {
        foreach (var name in names)
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }

    private static Npgsql.SslMode NpgsqlSslModeFrom(string value)
        => value.ToLowerInvariant() switch
        {
            "disable" => Npgsql.SslMode.Disable,
            "allow" => Npgsql.SslMode.Allow,
            "prefer" => Npgsql.SslMode.Prefer,
            "require" => Npgsql.SslMode.Require,
            "verify-ca" => Npgsql.SslMode.VerifyCA,
            "verify-full" => Npgsql.SslMode.VerifyFull,
            _ => throw new InvalidOperationException(
                $"Invalid '{ProjectDatabaseEnvironmentVariables.SslMode}' value '{value}'. Use disable, allow, prefer, require, verify-ca or verify-full."),
        };
}
