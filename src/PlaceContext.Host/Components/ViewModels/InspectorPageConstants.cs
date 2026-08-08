namespace PlaceContext.Host.Components.ViewModels;

internal static class InspectorPageConstants
{
    public const string Title = "MCP Inspector";
    public const string Subtitle = "live tool traffic · MCP via Streamable HTTP";
    public const int ToolCallLimit = 20;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
}
