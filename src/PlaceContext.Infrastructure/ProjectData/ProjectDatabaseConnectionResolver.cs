using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.ProjectData;

/// <summary>
/// Resolves which Postgres a project's data lives in. Defaults to the shared cluster database
/// (<c>PlaceContext:ConnectionString</c>); a per-project external database overrides it via Vault
/// secrets (the same Vault mechanism the OpenSearch resolver uses). The connection string is
/// composed server-side so credentials never appear in UI read models.
/// </summary>
public sealed class ProjectDatabaseConnectionResolver : IProjectDatabaseConnectionResolver
{
    public const string HostVariable = "PROJECT_DB_HOST";
    public const string PortVariable = "PROJECT_DB_PORT";
    public const string NameVariable = "PROJECT_DB_NAME";
    public const string UsernameVariable = "PROJECT_DB_USER";
    public const string PasswordVariable = "PROJECT_DB_PASSWORD";
    public const string SslModeVariable = "PROJECT_DB_SSL_MODE";

    public const string DefaultPort = "5432";
    public const string DefaultSslMode = "Prefer";

    private readonly PlaceContextOptions _options;
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;

    public ProjectDatabaseConnectionResolver(
        IOptions<PlaceContextOptions> options,
        IProjectSecretRepository secrets,
        ISecretProtector protector)
        => (_options, _secrets, _protector) = (options.Value, secrets, protector);

    public async Task<ProjectDatabaseConnection> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, cipher) in await _secrets.GetCiphersAsync(projectId, ct))
        {
            try { values[name] = _protector.Unprotect(cipher); }
            catch { /* A damaged unrelated Vault entry must not disable project data. */ }
        }

        var host = Value(values, HostVariable);
        if (string.IsNullOrWhiteSpace(host))
            return new ProjectDatabaseConnection(_options.ConnectionString, IsExternal: false);

        var user = Value(values, UsernameVariable);
        var password = Value(values, PasswordVariable);
        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException(
                $"'{UsernameVariable}' is required when an external database is configured.");
        if (password is null)
            throw new InvalidOperationException(
                $"'{PasswordVariable}' is required when an external database is configured.");

        var port = Value(values, PortVariable) ?? DefaultPort;
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new InvalidOperationException($"'{PortVariable}' must be a number.");

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.Parse(port, CultureInfo.InvariantCulture),
            Database = Value(values, NameVariable) ?? user,
            Username = user,
            Password = password,
            SslMode = NpgsqlSslModeFrom(Value(values, SslModeVariable) ?? DefaultSslMode),
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
                $"Invalid '{SslModeVariable}' value '{value}'. Use disable, allow, prefer, require, verify-ca or verify-full."),
        };
}
