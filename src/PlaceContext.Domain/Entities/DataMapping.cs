using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a declarative ingestion rule — maps a job's run data into one of the project's
/// database tables. Together the project's mappings form the data map: a tree of jobs whose
/// results consolidate into the appropriate tables. After every completed run of the source job,
/// rows are extracted from its primary artifact (optionally at <see cref="RowsPath"/>) and each
/// declared field lands in its target column. Generic — no domain knowledge of the data itself.
/// </summary>
public sealed class DataMapping : AggregateRoot
{
    private DataMapping(
        Guid id,
        Guid projectId,
        Guid jobId,
        string targetTable,
        string? rowsPath,
        IReadOnlyList<DataFieldMapping> fields,
        bool enabled,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        JobId = jobId;
        TargetTable = targetTable;
        RowsPath = rowsPath;
        Fields = fields;
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }

    /// <summary>The source job whose run results feed this mapping.</summary>
    public Guid JobId { get; }

    /// <summary>The project-database table rows are appended to (created on first ingest).</summary>
    public string TargetTable { get; private set; }

    /// <summary>
    /// Optional dot-path into the run's primary artifact locating the array of records to ingest
    /// (e.g. "rows" or "data.items"). Null = the artifact root (an array ingests per element; an
    /// object ingests as a single row).
    /// </summary>
    public string? RowsPath { get; private set; }

    /// <summary>The field mappings: where each column's value comes from in a record.</summary>
    public IReadOnlyList<DataFieldMapping> Fields { get; private set; }

    /// <summary>Disabled mappings are kept on the canvas but skipped at ingest time.</summary>
    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory: declares a new mapping from a job onto a project table.</summary>
    public static DataMapping Create(
        Guid projectId,
        Guid jobId,
        string targetTable,
        string? rowsPath,
        IReadOnlyList<DataFieldMapping> fields,
        DateTimeOffset createdAt,
        bool enabled = true)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (jobId == Guid.Empty)
            throw new ArgumentException("JobId must not be empty.", nameof(jobId));
        Validate(targetTable, fields);
        return new DataMapping(Guid.NewGuid(), projectId, jobId, targetTable.Trim(),
            Normalize(rowsPath), fields, enabled, createdAt, createdAt);
    }

    /// <summary>Rehydrates a mapping from persisted state. Infrastructure only.</summary>
    public static DataMapping Rehydrate(
        Guid id,
        Guid projectId,
        Guid jobId,
        string targetTable,
        string? rowsPath,
        IReadOnlyList<DataFieldMapping> fields,
        bool enabled,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        => new(id, projectId, jobId, targetTable, Normalize(rowsPath), fields, enabled, createdAt, updatedAt);

    /// <summary>Updates the mapping's target and fields. Validates the same invariants as Create.</summary>
    public void Update(string targetTable, string? rowsPath, IReadOnlyList<DataFieldMapping> fields,
        bool enabled, DateTimeOffset updatedAt)
    {
        Validate(targetTable, fields);
        TargetTable = targetTable.Trim();
        RowsPath = Normalize(rowsPath);
        Fields = fields;
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }

    private static void Validate(string targetTable, IReadOnlyList<DataFieldMapping> fields)
    {
        if (string.IsNullOrWhiteSpace(targetTable))
            throw new ArgumentException("Target table must not be empty.", nameof(targetTable));
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0)
            throw new ArgumentException("A mapping needs at least one field.", nameof(fields));
        var dup = fields.GroupBy(f => f.Column, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
            throw new ArgumentException($"Column '{dup.Key}' is mapped more than once.", nameof(fields));
    }

    private static string? Normalize(string? rowsPath)
        => string.IsNullOrWhiteSpace(rowsPath) ? null : rowsPath.Trim();
}
