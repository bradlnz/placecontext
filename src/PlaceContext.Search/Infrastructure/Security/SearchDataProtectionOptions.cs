namespace PlaceContext.Search.Infrastructure.Security;

public sealed class SearchDataProtectionOptions
{
    public const string SectionName = "PlaceContext:Search:DataProtection";

    public string? KeyDirectory { get; set; }
}
