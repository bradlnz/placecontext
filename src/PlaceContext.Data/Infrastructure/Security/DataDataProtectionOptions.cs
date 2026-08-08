namespace PlaceContext.Data.Infrastructure.Security;

public sealed class DataDataProtectionOptions
{
    public const string SectionName = "PlaceContext:Data:DataProtection";

    public string? KeyDirectory { get; set; }
}
