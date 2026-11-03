using Npgsql;

namespace PlaceContext.Infrastructure.DataSources;

internal static class SourceConnection
{
    public static string Validate(string value)
    {
        try
        {
            var supplied = new NpgsqlConnectionStringBuilder(value);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Host", "Port", "Database", "Username", "Password", "SSL Mode"
            };
            if (supplied.Keys.Cast<string>().Any(key => !allowed.Contains(key))
                || string.IsNullOrWhiteSpace(supplied.Host) || string.IsNullOrWhiteSpace(supplied.Database)
                || string.IsNullOrWhiteSpace(supplied.Username) || string.IsNullOrEmpty(supplied.Password)
                || supplied.Host.Contains('/') || supplied.Port is < 1 or > 65535)
            {
                throw new ArgumentException();
            }
            supplied.Timeout = 10;
            supplied.CommandTimeout = 30;
            supplied.Pooling = false;
            supplied.IncludeErrorDetail = false;
            return supplied.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Provide Host, Port, Database, Username, Password and optionally SSL Mode. Other connection options are not supported.");
        }
    }
}
