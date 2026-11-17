using System.Text.Json;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AssistantPageViewModel(PortalUiState ui) : PageViewModel
{
    public string Url { get; private set; } = "";
    public bool IsOpen { get; set; }
    public Guid? ProjectId => Url.Split('/', StringSplitOptions.RemoveEmptyEntries) is ["project", var id, ..]
        && Guid.TryParse(id, out var projectId) ? projectId : null;
    public string Kind => Active?.Kind ?? AgentForUrl(Url);
    public string Title => Kind switch
    {
        "code" => "Code agent", "data" => "Data agent", "observability" => "Observability agent",
        "workflow" => "Workflow agent", "workflow-manager" => "Workflow manager", "agents" => "Agent designer", "settings" => "Settings agent",
        "infrastructure" => "Infrastructure agent", "artifacts" => "Artifacts agent", _ => "Workspace agent"
    };
    public string StarterQuestion => Kind switch
    {
        "code" => "Explain this code and help me plan the next change.",
        "data" => "Help me write a query using the data on this page.",
        "observability" => "Analyze the available run data for failures, latency and retries.",
        "workflow" => "Explain this workflow and suggest improvements.",
        "workflow-manager" => "Help me create or manage workflows in this project.",
        "agents" => "Help me configure this agent and its linked workflows.",
        "settings" => "Help me configure the settings on this page.",
        "infrastructure" => "Help me understand and troubleshoot this infrastructure.",
        "artifacts" => "Help me understand the reports and files on this page.",
        _ => "Help me plan the next step for this project."
    };
    public long Version { get; private set; }
    public Func<string, Task>? Apply => Active?.Apply;
    public Func<Task>? Refresh => Active?.Refresh;
    private sealed record Registration(object Owner, Func<Task<string>> Read, Func<string, Task>? Apply, Func<Task>? Refresh, string? Kind);
    private readonly List<Registration> _contexts = [];
    private Registration? Active => _contexts.LastOrDefault();

    public void Navigate(string relativeUrl)
    {
        if (Url == relativeUrl) return;
        Url = relativeUrl;
        _contexts.Clear();
        Version++;
        NotifyStateChanged();
    }

    public void Register(object owner, Func<Task<string>> readContext, Func<string, Task>? apply, Func<Task>? refresh = null, string? kind = null)
    {
        var previous = Active;
        var index = _contexts.FindIndex(context => ReferenceEquals(context.Owner, owner));
        var registration = new Registration(owner, readContext, apply, refresh, kind);
        if (index < 0) _contexts.Add(registration);
        else _contexts[index] = registration;
        if (!ReferenceEquals(previous?.Owner, Active?.Owner) || previous?.Kind != Active?.Kind)
        {
            Version++;
            NotifyStateChanged();
        }
    }

    public void Unregister(object owner)
    {
        var active = ReferenceEquals(Active?.Owner, owner);
        _contexts.RemoveAll(context => ReferenceEquals(context.Owner, owner));
        if (!active) return;
        Version++;
        NotifyStateChanged();
    }

    public async Task<string> ReadAsync()
    {
        var version = Version;
        var context = Active is { } active ? await active.Read() : "No page records or editor contents were supplied. Ask for the details needed; do not invent page contents.";
        if (version != Version) throw new InvalidOperationException("The page changed. Send your question again on the current page.");
        return JsonSerializer.Serialize(new { url = Url, agent = Kind, page = ui.Title,
            project = ui.CurrentProjectName, projectId = ui.CurrentProjectId, context });
    }

    public static string AgentForUrl(string relativeUrl)
    {
        var path = relativeUrl.Split('?', '#')[0].Trim('/').ToLowerInvariant();
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var project = parts.Length >= 3 && parts[0] == "project" && Guid.TryParse(parts[1], out _);
        var section = project ? parts[2] : parts.FirstOrDefault() ?? "";
        if (project && parts.Length == 3 && section is "workflows" or "chains") return "workflow-manager";
        if (project && section is "jobs" or "tests" && parts.Length == 4 && Guid.TryParse(parts[3], out _)) return "code";
        if (project && section == "dashboard" && parts.ElementAtOrDefault(3) == "settings") return "agents";
        if (project && section is "workflows" or "chains" or "agents" && parts.Skip(3).Any(part => part is "observability" or "runs")) return "observability";
        return section switch
        {
            "observability" => "observability",
            "data" or "data-search" or "data-graph" or "entities" or "entity" or "datamap" or "sources" or "analytics" or "inspector" => "data",
            "workflows" or "chains" or "schedules" or "events" => "workflow",
            "agents" => "agents",
            "settings" or "onboarding" => "settings",
            "cluster" or "cloud" or "jobs" or "tests" or "queues" or "networking" or "secrets" => "infrastructure",
            "artifacts" => "artifacts",
            "build" when parts.Length > 3 && parts[3] == "workflows" => "workflow",
            _ => "workspace"
        };
    }
}
