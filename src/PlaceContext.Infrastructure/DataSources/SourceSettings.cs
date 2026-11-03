using System.Text.Json;
using Npgsql;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Infrastructure.DataSources;

internal static class SourceSettings
{
    public static Dictionary<string, string> Parse(string provider, string value)
    {
        var definition = SourceProviders.Get(provider);
        Dictionary<string, string> settings;
        try
        {
            settings = JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? throw new ArgumentException();
        }
        catch (JsonException)
        {
            throw new ArgumentException("Connection settings must be a JSON object.");
        }
        if (value.Length > 16000 || settings.Any(pair => pair.Value is null || pair.Value.Length > 8192)
            || settings.Keys.Any(key => !definition.Fields.Any(field => field.Key == key) && !(definition.Kind == "feed" && key == "token")))
        {
            throw new ArgumentException("Invalid source connection fields.");
        }
        foreach (var field in definition.Fields.Where(field => field.Key is not ("prefix" or "endpoint")))
        {
            if (string.IsNullOrWhiteSpace(settings.GetValueOrDefault(field.Key)))
            {
                throw new ArgumentException($"Enter {field.Label}.");
            }
        }
        if (definition.Kind == "feed")
        {
            if (!settings.TryGetValue("token", out var token) || token.Length < 43 || token.Length > 128)
            {
                throw new ArgumentException("Provide a feed token of 43–128 characters.");
            }
        }
        if (definition.Kind == "database" && (!int.TryParse(settings["port"], out var port) || port is < 1 or > 65535
            || Uri.CheckHostName(settings["host"]) == UriHostNameType.Unknown))
        {
            throw new ArgumentException("Enter a hostname or IP address and a valid port.");
        }
        if (definition.Kind is "files" or "warehouse" && (provider != "s3" || !string.IsNullOrEmpty(settings.GetValueOrDefault("endpoint"))))
        {
            if (!Uri.TryCreate(settings.GetValueOrDefault("endpoint"), UriKind.Absolute, out var uri)
                || (uri.Scheme != "https" && !(provider == "s3" && uri.Scheme == "http" && uri.IsLoopback))
                || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            {
                throw new ArgumentException("Use an HTTPS service URL without a path, credentials or query.");
            }
            if (provider is "adls" or "fabric" && !uri.Host.EndsWith(".dfs.core.windows.net", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".dfs.fabric.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Use an Azure Data Lake or OneLake DFS endpoint.");
            }
        }
        if (provider is "adls" or "fabric" && (!Guid.TryParse(settings["tenant"], out _) || !Guid.TryParse(settings["username"], out _)))
        {
            throw new ArgumentException("Use tenant and application client IDs from Microsoft Entra.");
        }
        return settings;
    }

    public static string Validate(string provider, string value)
    {
        if (provider == "postgres" && !value.TrimStart().StartsWith('{'))
        {
            return SourceConnection.Validate(value);
        }
        var settings = Parse(provider, value);
        if (provider == "postgres")
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = settings["host"], Port = int.Parse(settings["port"]), Database = settings["database"],
                Username = settings["username"], Password = settings["password"], SslMode = SslMode.VerifyFull,
                Timeout = 10, CommandTimeout = 30, Pooling = false, IncludeErrorDetail = false
            }.ConnectionString;
        }
        return JsonSerializer.Serialize(settings);
    }
}
