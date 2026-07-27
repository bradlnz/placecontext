namespace PlaceContext.Host.Components.ViewModels;

public sealed class ClarificationRequest
{
    public string ToolName { get; set; } = "";
    public string Args { get; set; } = "";
    public string Question { get; set; } = "";
    public List<ClarificationOption> Options { get; set; } = new();
    public bool MultiSelect { get; set; }
}
