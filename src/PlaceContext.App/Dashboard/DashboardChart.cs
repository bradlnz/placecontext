using System.Text.Json;

namespace PlaceContext.App.Dashboard;

public sealed record DashboardChart(string Name, JsonElement Spec, DateTimeOffset GeneratedAt);
