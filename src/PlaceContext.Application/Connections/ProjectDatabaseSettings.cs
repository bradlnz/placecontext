namespace PlaceContext.Application.Connections;

public static class ProjectDatabaseSettings
{
    public const string HostVariable = "PROJECT_DB_HOST";
    public const string PortVariable = "PROJECT_DB_PORT";
    public const string NameVariable = "PROJECT_DB_NAME";
    public const string UsernameVariable = "PROJECT_DB_USER";
    public const string PasswordVariable = "PROJECT_DB_PASSWORD";
    public const string SslModeVariable = "PROJECT_DB_SSL_MODE";
    public const string DefaultPort = "5432";
    public const string DefaultSslMode = "Prefer";
}
