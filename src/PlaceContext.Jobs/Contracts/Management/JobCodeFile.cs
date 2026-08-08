namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>One source file in a code workload (map or reduce step).</summary>
public sealed record JobCodeFile(string Path, string Content);
