using System.Text.Json;
using System.Text.Json.Serialization;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>One named series of a chart spec.</summary>
public sealed record ChartSeries(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("values")] IReadOnlyList<double> Values);
