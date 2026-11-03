namespace PlaceContext.Application.DataSources;

public sealed record SourceConnectionField(string Key, string Label, bool Secret = false, string Default = "");
