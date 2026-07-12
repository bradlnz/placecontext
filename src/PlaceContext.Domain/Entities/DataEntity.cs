using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a business entity tag over ingested project data — "sites", "listings",
/// "customers". It names a table (or view) in the project's database, picks the column that labels
/// a record, and declares how records relate to OTHER entities (a column here matches a column
/// there). Tagged entities surface as custom menu items rendering business-facing views.
/// Generic — no domain knowledge of the data itself.
/// </summary>
public sealed class DataEntity : AggregateRoot
{
    private DataEntity(Guid id, Guid projectId, string name, string tableName, string? labelColumn,
        IReadOnlyList<EntityRelation> relations, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        TableName = tableName;
        LabelColumn = labelColumn;
        Relations = relations;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }

    /// <summary>The business name — also the menu label (e.g. "Sites").</summary>
    public string Name { get; private set; }

    /// <summary>The table or view in the project database holding this entity's records.</summary>
    public string TableName { get; private set; }

    /// <summary>The column whose value labels a record in lists/detail. Null = first column.</summary>
    public string? LabelColumn { get; private set; }

    /// <summary>How records relate to other entities (this column ↔ that entity's column).</summary>
    public IReadOnlyList<EntityRelation> Relations { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static DataEntity Create(Guid projectId, string name, string tableName, string? labelColumn,
        IReadOnlyList<EntityRelation> relations, DateTimeOffset createdAt)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        Validate(name, tableName);
        return new DataEntity(Guid.NewGuid(), projectId, name.Trim(), tableName.Trim(), Trim(labelColumn),
            relations, createdAt, createdAt);
    }

    public static DataEntity Rehydrate(Guid id, Guid projectId, string name, string tableName,
        string? labelColumn, IReadOnlyList<EntityRelation> relations,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, tableName, labelColumn, relations, createdAt, updatedAt);

    public void Update(string name, string tableName, string? labelColumn,
        IReadOnlyList<EntityRelation> relations, DateTimeOffset updatedAt)
    {
        Validate(name, tableName);
        Name = name.Trim();
        TableName = tableName.Trim();
        LabelColumn = Trim(labelColumn);
        Relations = relations;
        UpdatedAt = updatedAt;
    }

    private static void Validate(string name, string tableName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An entity needs a name.", nameof(name));
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("An entity needs a source table or view.", nameof(tableName));
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
