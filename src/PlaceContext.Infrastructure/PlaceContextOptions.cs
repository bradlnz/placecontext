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

    /// <summary>
    /// Durable on-disk product data. Separate from <see cref="WorkspaceRoot"/> so cloned repos stay
    /// distinct from Host-owned files.
    /// </summary>
    public string DataRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".placecontext", "data");
}
