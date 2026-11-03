namespace PlaceContext.Application.DataSources;

public sealed record SourceProvider(string Id, string Name, string Kind, IReadOnlyList<SourceConnectionField> Fields);
