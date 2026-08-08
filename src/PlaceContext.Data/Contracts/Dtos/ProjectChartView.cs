namespace PlaceContext.Application.Features;

/// <summary>One stored analytics chart, ready to render (self-contained themed HTML document).</summary>
public sealed record ProjectChartView(string TableName, string Html, DateTimeOffset GeneratedAt);
