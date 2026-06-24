namespace PlaceContext.Infrastructure;

/// <summary>Configuration for the PlaceContext infrastructure. Bound from the "PlaceContext" config section.</summary>
public sealed class PlaceContextOptions
{
    /// <summary>PostgreSQL connection string for the EF Core store.</summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=placecontext;Username=postgres;Password=postgres";

    /// <summary>Root folder under which tenant repositories are cloned (one subfolder per tenant).</summary>
    public string WorkspaceRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".placecontext", "workspaces");

    /// <summary>GitHub OAuth app settings for importing repositories.</summary>
    public GitHubOptions GitHub { get; set; } = new();
}
