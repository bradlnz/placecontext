namespace PlaceContext.Jobs.Infrastructure.Workload;

public sealed record WorkloadDependencyRecipe(
    string Manifest,
    bool NeedsWritableApp,
    string SetupTemplate,
    string EnvTemplate,
    string InstallTemplate,
    IReadOnlyList<string> Companions,
    string BakeInstall,
    string BakeEnv,
    string? InvokePrefix = null);
