namespace PlaceContext.Application.DataSources;

public sealed record DataSourceDefinition(Guid Id, Guid ProjectId, string Name, string Schema, string Table,
    string KeyColumn, IReadOnlyList<string> Columns, IReadOnlyList<string> Tags, bool EnableEmbeddings,
    Guid Version, DateTimeOffset? SyncedAt = null, int RecordCount = 0, Guid? CreatedBy = null, DateTimeOffset? CreatedAt = null,
    string Provider = "postgres", SourceProcessingPolicy? Processing = null, SourceExecution? Execution = null,
    SourceQueueConfiguration? Queue = null);
