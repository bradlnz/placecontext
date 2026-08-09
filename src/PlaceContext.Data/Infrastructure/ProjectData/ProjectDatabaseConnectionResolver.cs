using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Infrastructure.Persistence;
using PlaceContext.Data.Integration;

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

    private readonly DataPersistenceOptions _options;
    private readonly IDataVaultClient? _vault;

    public ProjectDatabaseConnectionResolver(
        IOptions<DataPersistenceOptions> options,
        IDataVaultClient? vault = null)
        => (_options, _vault) = (options.Value, vault);

    public async Task<ProjectDatabaseConnection> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_vault is not null)
        {
            string[] names =
            [
                ProjectDatabaseEnvironmentVariables.Host,
                ProjectDatabaseEnvironmentVariables.Port,
                ProjectDatabaseEnvironmentVariables.Name,
                ProjectDatabaseEnvironmentVariables.Username,
                ProjectDatabaseEnvironmentVariables.Password,
                ProjectDatabaseEnvironmentVariables.SslMode,
            ];
            try
            {
                foreach (var pair in await _vault.GetSecretsAsync(projectId, names, ct))
                    values[pair.Key] = pair.Value;
            }
            catch (HttpRequestException) { /* Vault unavailable: use the cluster database. */ }
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
