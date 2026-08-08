namespace PlaceContext.Vault.Infrastructure.Persistence;

public sealed class VaultPersistenceOptions
{
    public const string SectionName = "PlaceContext:Vault:Persistence";
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=placecontext_vault;Username=postgres;Password=postgres";

    public string ConnectionString { get; set; } = DefaultConnectionString;
}
