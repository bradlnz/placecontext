using PlaceContext.Application.Features;

namespace PlaceContext.Application.Build;

public sealed class DashboardSettings : System.Text.Json.Serialization.IJsonOnDeserialized
{
    public static readonly Guid DefaultAgentId = new("c471b57a-35ae-4c91-a9d5-45783182b874");

    public string Instructions { get; set; } = "";
    public List<DashboardAgentProfile> Agents { get; set; } = [new() { Id = DefaultAgentId, Name = "Assistant" }];
    public List<DashboardAction> Actions { get; set; } =
    [
        new("Job statistics", "Show this project's job statistics using project_activity. Do not run or change anything."),
        new("See artifacts", "Show this project's recent artifacts and help me find the output I need. Do not run or change anything.")
    ];
    public List<string> TileRowStarts { get; set; } = [];
    public List<string> TileOrder { get; set; } = [];
    public Dictionary<string, string> TileSizes { get; set; } = [];
    public List<DashboardChart> Charts { get; set; } = [];

    public IEnumerable<string> TileKeys() => new[] { "workflow" }.Concat(Actions.Select(a => a.Id.ToString())).Append("chat").Concat(Charts.Select(c => c.Id.ToString()));
    public IReadOnlyList<string> OrderedTiles() => TileOrder.Concat(TileKeys()).Distinct().Intersect(TileKeys()).ToArray();
    public string TileSize(string key) => TileSizes.GetValueOrDefault(key) ?? (Charts.Any(c => c.Id.ToString() == key) ? "large" : "square");

    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        // Preserve settings saved before the default agent had a nonempty identity.
        foreach (var agent in Agents ?? [])
            if (agent is { Id: var id } && id == Guid.Empty) agent.Id = DefaultAgentId;
    }

    public void Validate()
    {
        foreach (var agent in Agents ?? []) agent?.Validate();
        if (Agents is null || Agents.Count is < 1 or > 8 || Agents.Any(a => a is null || a.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60
                || a.Model is null || a.Model.Length > 200 || a.Instructions is null || a.Instructions.Length > 8000
                || a.McpConnectionIds is null || a.McpConnectionIds.Count > 32 || a.McpConnectionIds.Contains(Guid.Empty)
                || a.McpConnectionIds.Distinct().Count() != a.McpConnectionIds.Count)
            || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count
            || Agents.Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Agents.Count
            || !Agents.Any(a => a.Id == DefaultAgentId))
        {
            throw new ArgumentException("Keep the default agent and use up to 8 agents with unique names up to 60 characters, models up to 200, instructions up to 8,000, and up to 32 distinct MCP connections each.");
        }
        if (Instructions is null || Instructions.Length > 8000 || Actions is null || Actions.Count > 8
            || Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 60
                || string.IsNullOrWhiteSpace(a.Prompt) || a.Prompt.Length > 2000))
        {
            throw new ArgumentException("Use up to 8 tiles, with titles up to 60 characters, prompts up to 2,000, and instructions up to 8,000.");
        }
        if (Charts is null || Charts.Count > 8 || Charts.Any(c => c is null || c.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(c.Source) || c.Source.Length > 2000
            || c.ChartJson is null || c.ChartJson.Length > 32000 || ChartSpec.TryParse(c.ChartJson) is null
            || c.Sql is not null && (string.IsNullOrWhiteSpace(c.Sql) || c.Sql.Length > 32768)))
        {
            throw new ArgumentException("Pin up to 8 valid charts with a source up to 2,000 characters.");
        }
        if (Actions.Any(a => a.Id == Guid.Empty) || TileKeys().Distinct().Count() != Actions.Count + Charts.Count + 2
            || TileRowStarts is null || TileRowStarts.Count > 18 || TileRowStarts.Distinct().Count() != TileRowStarts.Count
            || TileRowStarts.Except(TileKeys()).Any()
            || TileOrder is null || TileOrder.Count > 18 || TileOrder.Distinct().Count() != TileOrder.Count
            || TileOrder.Except(TileKeys()).Any()
            || TileSizes is null || TileSizes.Count > 18 || TileSizes.Keys.Except(TileKeys()).Any()
            || TileSizes.Values.Any(size => size is not ("square" or "wide" or "tall" or "large")))
        {
            throw new ArgumentException("Use each dashboard tile once in the layout.");
        }
    }
}
