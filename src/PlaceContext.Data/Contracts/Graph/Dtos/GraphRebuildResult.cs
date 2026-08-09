namespace PlaceContext.Data.Contracts.Graph;

/// <summary>Data-owned result returned after rebuilding a project's knowledge graph.</summary>
public sealed record GraphRebuildResult(
    Guid ProjectId,
    string ProjectName,
    string ProjectPath,
    string ProjectStatus,
    bool IsGraphified,
    int GodNodeCount,
    int NodeCount,
    int LinkCount);
