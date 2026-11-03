using System.Text.Json.Serialization;
using System.Text.Json;
namespace PlaceContext.Application.Features;

/// <summary>One named series of a chart spec.</summary>
public sealed record ChartSeries(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("values")] IReadOnlyList<double> Values);
