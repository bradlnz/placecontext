using System.Text.Json;

namespace PlaceContext.Host.Controllers;

public sealed record DashboardChart(
    string Name,
    JsonElement Spec,
    DateTimeOffset GeneratedAt);
