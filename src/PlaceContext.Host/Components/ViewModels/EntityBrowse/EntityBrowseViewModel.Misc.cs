using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel
{
    // ── Entity state ──────────────────────────────────────────────────────────────────────────
    public DataEntityView? Entity { get; private set; }
    public IReadOnlyList<DataEntityView> AllEntities { get; private set; } =
        Array.Empty<DataEntityView>();
    public ProjectQueryResult? Rows { get; private set; }
    public IReadOnlyList<string?>? Open { get; private set; }
    public List<(EntityRelationDto Relation, ProjectQueryResult Rows)> Related { get; } = new();
    public IReadOnlyList<RecordLink> AutoLinks { get; private set; } = Array.Empty<RecordLink>();
    public string? Error { get; private set; }
    public string? WarnMessage { get; private set; }
    public bool Loaded { get; private set; }

    // ── Edit state ────────────────────────────────────────────────────────────────────────────
    public bool Editing { get; private set; }
    public bool Creating { get; private set; }
    public bool Saving { get; private set; }
    public string? EditError { get; private set; }

    public List<FormColumn> FormColumns { get; private set; } = new();
    public Dictionary<string, string?> FormValues { get; private set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, string?> KeySnapshot { get; private set; } =
        new(StringComparer.Ordinal);
}
