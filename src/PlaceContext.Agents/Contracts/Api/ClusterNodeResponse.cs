namespace PlaceContext.Agents.Contracts.Api;

public sealed record ClusterNodeResponse(
    string Name,
    IReadOnlyList<string> Roles,
    bool Ready,
    string KubeletVersion,
    string PreferredIp,
    string CpuCapacity,
    string MemoryCapacity,
    bool IsSelf,
    bool IsControlPlane,
    bool IsDesignatedMaster,
    string PlatformLabel,
    string RelativeAge);
