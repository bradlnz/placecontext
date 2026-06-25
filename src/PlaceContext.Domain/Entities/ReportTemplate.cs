using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a <b>defined report</b> — a named, ordered set of <see cref="ReportSection"/>s the
/// generation layer fills from a project's accumulated data. Tenant-owned and editable from the portal;
/// built-in templates (see <c>BuiltInReportTemplates</c>) are available to every tenant out of the box.
/// </summary>
public sealed class ReportTemplate : AggregateRoot
{
    private readonly List<ReportSection> _sections;

    private ReportTemplate(
        Guid id, string name, string description, IEnumerable<ReportSection> sections,
        bool isBuiltIn, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        Description = description;
        _sections = sections.ToList();
        IsBuiltIn = isBuiltIn;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public IReadOnlyList<ReportSection> Sections => _sections;
    /// <summary>True for the code-defined defaults that are not stored in the database.</summary>
    public bool IsBuiltIn { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReportTemplate Create(string name, string description, IEnumerable<ReportSection> sections, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Report template name is required.", nameof(name));
        var list = sections?.ToList() ?? new List<ReportSection>();
        if (list.Count == 0) throw new ArgumentException("A report template needs at least one section.", nameof(sections));
        return new ReportTemplate(Guid.NewGuid(), name.Trim(), description?.Trim() ?? string.Empty, list, isBuiltIn: false, now, now);
    }

    /// <summary>Builds a code-defined default template (not persisted).</summary>
    public static ReportTemplate BuiltIn(Guid id, string name, string description, IEnumerable<ReportSection> sections)
        => new(id, name, description, sections, isBuiltIn: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public static ReportTemplate Rehydrate(
        Guid id, string name, string description, IEnumerable<ReportSection> sections, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, name, description ?? string.Empty, sections, isBuiltIn: false, createdAt, updatedAt);

    public void Replace(string description, IEnumerable<ReportSection> sections, DateTimeOffset now)
    {
        Description = description?.Trim() ?? string.Empty;
        var list = sections?.ToList() ?? new List<ReportSection>();
        if (list.Count == 0) throw new ArgumentException("A report template needs at least one section.", nameof(sections));
        _sections.Clear();
        _sections.AddRange(list);
        UpdatedAt = now;
    }
}
