namespace PlaceContext.Application.DataSources;

public static class SourceProviders
{
    public static IReadOnlyList<SourceProvider> All { get; } =
    [
        new("postgres", "PostgreSQL", "database", DatabaseFields("5432")),
        new("mysql", "MySQL", "database", DatabaseFields("3306")),
        new("mssql", "SQL Server", "database", DatabaseFields("1433")),
        new("s3", "Amazon S3 / MinIO", "files", [new("endpoint", "S3 endpoint (optional for AWS)"), new("region", "Region", Default: "us-east-1"), new("bucket", "Bucket"), new("prefix", "Folder prefix"), new("username", "Access key", true), new("password", "Secret key", true)]),
        new("adls", "Azure Data Lake", "files", LakeFields("")),
        new("fabric", "Microsoft Fabric / OneLake", "files", LakeFields("https://onelake.dfs.fabric.microsoft.com")),
        new("snowflake", "Snowflake", "warehouse", [new("endpoint", "Account URL"), new("token", "OAuth access token", true), new("warehouse", "Warehouse"), new("database", "Database")]),
        new("databricks", "Databricks", "warehouse", [new("endpoint", "Workspace URL"), new("token", "Access token", true), new("warehouse", "SQL warehouse ID"), new("database", "Catalog")]),
        new("email", "Inbound email", "feed", []),
        new("feed", "API / event feed", "feed", [])
    ];

    public static SourceProvider Get(string id) => All.FirstOrDefault(provider => provider.Id == id)
        ?? throw new ArgumentException("Choose a supported source provider.");

    private static SourceConnectionField[] DatabaseFields(string port) =>
        [new("host", "Host"), new("port", "Port", Default: port), new("database", "Database"), new("username", "Read-only user"), new("password", "Password", true)];

    private static SourceConnectionField[] LakeFields(string endpoint) =>
        [new("endpoint", "Storage URL", Default: endpoint), new("bucket", "Filesystem / workspace"), new("prefix", "Folder / lakehouse path"), new("tenant", "Entra tenant ID"), new("username", "Application client ID"), new("password", "Client secret", true)];
}
