namespace PlaceContext.Application.Build;

public sealed class DashboardSettings
{
    public string Instructions { get; set; } = "";
    public List<DashboardAction> Actions { get; set; } =
    [
        new("Job statistics", "Show this project's job statistics using project_activity. Do not run or change anything."),
        new("See artifacts", "Show this project's recent artifacts and help me find the output I need. Do not run or change anything.")
    ];

    public void Validate()
    {
        if (Instructions is null || Instructions.Length > 8000 || Actions is null || Actions.Count > 8
            || Actions.Any(a => a is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 60
                || string.IsNullOrWhiteSpace(a.Prompt) || a.Prompt.Length > 2000))
        {
            throw new ArgumentException("Use up to 8 tiles, with titles up to 60 characters, prompts up to 2,000, and instructions up to 8,000.");
        }
    }
}
