using System.Text.RegularExpressions;

namespace PlaceContext.Application.DataSources;

public static partial class DataSourceRules
{
    // shortcut: bounded snapshots and in-process ranking, use streaming ingestion and an indexed store above these limits.
    public const int MaxRecords = 1000;
    public const int MaxQueuedPostgresRecords = 100000;
    public const int MaxSources = 25;
    public const int MaxRecordChars = 16000;
    public const int ChunkChars = 2000;

    public static int RecordLimit(DataSourceDefinition source)
        => source.Provider == "postgres" && source.Queue is not null ? MaxQueuedPostgresRecords : MaxRecords;

    public static int SnapshotCharLimit(DataSourceDefinition source)
        => source.Provider == "postgres" && source.Queue is not null ? 32_000_000 : 8_000_000;

    public static void Validate(DataSourceDefinition source)
    {
        SourceProviders.Get(source.Provider);
        if (source.Queue is { } queue)
        {
            if (queue.QueueId == Guid.Empty || queue.BatchSize is < 1 or > 250 || queue.RequiredFields is null || queue.RedactedFields is null
                || queue.RequiredFields.Count > 40 || queue.RedactedFields.Count > 40
                || source.Columns is null || queue.RequiredFields.Concat(queue.RedactedFields).Any(field => !source.Columns.Contains(field))
                || queue.RedactedFields.Contains(source.KeyColumn))
            {
                throw new ArgumentException("Choose a queue, a batch size of 1–250, and approved fields for validation/redaction. The unique key cannot be redacted.");
            }
            if (source.EnableEmbeddings || source.Processing is not null)
            {
                throw new ArgumentException("Queue sources prepare data only. Configure model processing and retrieval in the destination workflow.");
            }
        }
        if (source.Processing is { } policy && (string.IsNullOrWhiteSpace(policy.Criteria) || policy.Criteria.Length > 3000
            || !double.IsFinite(policy.MinimumConfidence) || policy.MinimumConfidence is < 0.5 or > 1))
        {
            throw new ArgumentException("Provide Jev acceptance criteria of 1–3,000 characters and a confidence threshold from 0.5 to 1.");
        }
        if (SourceProviders.Get(source.Provider).Kind is "feed" or "files" && source.KeyColumn != "id")
        {
            throw new ArgumentException("Feeds and file datasets use the id field as their unique key.");
        }
        if (source.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 100)
        {
            throw new ArgumentException("A project and a source name of 1–100 characters are required.");
        }
        if (source.Columns is null || source.Tags is null)
        {
            throw new ArgumentException("Columns and tags are required.");
        }
        var identifiers = source.Columns.Concat([source.Schema, source.Table, source.KeyColumn]);
        if (source.Columns.Count is < 1 or > 40 || identifiers.Any(value => (value is null || !Identifier().IsMatch(value)))
            || !source.Columns.Contains(source.KeyColumn, StringComparer.Ordinal)
            || source.Columns.Distinct(StringComparer.Ordinal).Count() != source.Columns.Count)
        {
            throw new ArgumentException("Select 1–40 distinct columns including a unique key. Use identifiers of up to 63 letters, digits or underscores.");
        }
        if (source.Tags.Count > 20 || source.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 80))
        {
            throw new ArgumentException("Use up to 20 non-empty tags of at most 80 characters.");
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
