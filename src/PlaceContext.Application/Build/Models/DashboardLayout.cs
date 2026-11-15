using PlaceContext.Application.Features;

namespace PlaceContext.Application.Build;

public sealed record DashboardTilePosition(int Column, int Row);

public class DashboardLayout
{
    public Dictionary<string, DashboardAction> BuiltInTiles { get; set; } = new()
    {
        ["workflow"] = new("Run a workflow", "") { Id = Guid.Empty, Description = "Choose a saved workflow and run it with the assistant." },
        ["chat"] = new("Ask a question", "") { Id = Guid.Empty, Description = "Ask about your project or get help with a task." },
        ["chart"] = new("Add chart", "Help me create a live chart for my dashboard. Ask what I want to see, then create a chart I can preview and pin.")
            { Id = Guid.Empty, Description = "Create a chart with the assistant and pin it to your dashboard." }
    };
    public List<DashboardAction> Actions { get; set; } =
    [
        new("Job statistics", "Show this project's job statistics using project_activity. Do not run or change anything."),
        new("See artifacts", "Show this project's recent artifacts and help me find the output I need. Do not run or change anything.")
    ];
    public List<string> HiddenTiles { get; set; } = [];
    public Dictionary<string, DashboardTilePosition> TilePositions { get; set; } = [];
    public List<string> TileRowStarts { get; set; } = [];
    public List<string> TileOrder { get; set; } = [];
    public Dictionary<string, string> TileSizes { get; set; } = [];
    public List<DashboardChart> Charts { get; set; } = [];

    public IEnumerable<string> TileKeys() => new[] { "workflow" }.Concat(Actions.Select(a => a.Id.ToString())).Append("chat").Append("chart").Concat(Charts.Select(c => c.Id.ToString()));
    public IReadOnlyList<string> OrderedTiles() => TileOrder.Concat(TileKeys()).Distinct().Intersect(TileKeys()).Except(HiddenTiles).ToArray();
    public string TileSize(string key) => TileSizes.GetValueOrDefault(key) ?? (Charts.Any(c => c.Id.ToString() == key) ? "large" : "square");
    public DashboardAction? TileAction(string key) => BuiltInTiles.GetValueOrDefault(key) ?? Actions.FirstOrDefault(a => a.Id.ToString() == key);

    public void ValidateLayout()
    {
        if (BuiltInTiles is null || BuiltInTiles.Count != 3 || new[] { "workflow", "chat", "chart" }.Except(BuiltInTiles.Keys).Any()
            || BuiltInTiles.Values.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 60
                || a.Description?.Length > 300 || a.Prompt is null || a.Prompt.Length > 2000)
            || string.IsNullOrWhiteSpace(BuiltInTiles["chart"].Prompt))
        {
            throw new ArgumentException("Use titles up to 60 characters, descriptions up to 300, and prompts up to 2,000. Add chart needs a prompt.");
        }
        if (Actions is null || Actions.Count > 8
            || Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 60
                || string.IsNullOrWhiteSpace(a.Prompt) || a.Prompt.Length > 2000 || a.Description?.Length > 300))
        {
            throw new ArgumentException("Use up to 8 tiles, with titles up to 60 characters, prompts up to 2,000.");
        }
        if (Charts is null || Charts.Count > 8 || Charts.Any(c => c is null || c.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(c.Source) || c.Source.Length > 2000
            || c.ChartJson is null || c.ChartJson.Length > 32000 || ChartSpec.TryParse(c.ChartJson) is null
            || c.Sql is not null && (string.IsNullOrWhiteSpace(c.Sql) || c.Sql.Length > 32768)))
        {
            throw new ArgumentException("Pin up to 8 valid charts with a source up to 2,000 characters.");
        }
        if (Actions.Any(a => a.Id == Guid.Empty) || TileKeys().Distinct().Count() != Actions.Count + Charts.Count + 3
            || HiddenTiles is null || HiddenTiles.Count > 19 || HiddenTiles.Distinct().Count() != HiddenTiles.Count
            || HiddenTiles.Except(TileKeys()).Any()
            || TileRowStarts is null || TileRowStarts.Count > 19 || TileRowStarts.Distinct().Count() != TileRowStarts.Count
            || TileRowStarts.Except(TileKeys()).Any()
            || TileOrder is null || TileOrder.Count > 19 || TileOrder.Distinct().Count() != TileOrder.Count
            || TileOrder.Except(TileKeys()).Any()
            || TilePositions is null || TilePositions.Count > 19 || TilePositions.Keys.Except(TileKeys()).Any()
            || TilePositions.Values.Any(p => p is null || p.Column is < 1 or > 32 || p.Row is < 1 or > 100)
            || TileSizes is null || TileSizes.Count > 19 || TileSizes.Keys.Except(TileKeys()).Any()
            || TileSizes.Values.Any(size => size is not ("square" or "wide" or "tall" or "large")))
        {
            throw new ArgumentException("Use each dashboard tile once in the layout.");
        }
    }
}
