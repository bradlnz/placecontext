namespace PlaceContext.Host.Api;

public sealed record CreateDataSourceRequest(string Name, string ConnectionString, string Schema, string Table,
    string KeyColumn, string[] Columns, string[] Tags, bool EnableEmbeddings = false, string Provider = "postgres",
    ConfigureSourceProcessingRequest? Processing = null, ConfigureSourceQueueRequest? Queue = null);
