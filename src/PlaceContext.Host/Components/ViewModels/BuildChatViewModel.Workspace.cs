using System.Text.Json.Nodes;
using System.Text.Json;
using PlaceContext.Application.Access;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public sealed record WorkspaceItem(Guid Id, string Kind, string Name);
    public sealed record ChartPreview(ChartSpec Spec, string Source);
    public ChartPreview? PreviewChart
    {
        get; private set;
    }
    public ChartPreview? ResultChart(JsonNode message)
    {
        var callId = message["tool_call_id"]?.ToString();
        if (callId is null || !Conversation.Messages.OfType<JsonNode>().SelectMany(Calls)
            .Any(call => call["id"]?.ToString() == callId && call["function"]?["name"]?.ToString() == "show_chart"))
        {
            return null;
        }
        try
        {
            var result = JsonNode.Parse(Text(message));
            var spec = result?["chart"]?.ToJsonString();
            var source = result?["source"]?.GetValue<string>();
            return spec is not null && ChartSpec.TryParse(spec) is { } chart && !string.IsNullOrWhiteSpace(source)
                ? new(chart, source) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
    public void OpenChart(ChartPreview chart)
    {
        CloseWorkspace();
        OpenWorkspace();
        PreviewChart = chart;
    }

    public WorkspaceItem? PreviewItem
    {
        get; private set;
    }
    public JobChainView? PreviewWorkflow
    {
        get; private set;
    }
    public JobView? PreviewJob
    {
        get; private set;
    }
    public bool PreviewLoading
    {
        get; private set;
    }
    public string? PreviewError
    {
        get; private set;
    }
    public string? PreviewEditorUrl => PreviewItem is not { } item ? null
        : $"/project/{Conversation.ProjectId}/{(item.Kind == "workflow" ? "workflows?workflow" : "jobs?job")}={item.Id}";
    private int _previewVersion;
    private string? _autoPreviewCall;

    public IReadOnlyList<WorkspaceItem> ResultItems(JsonNode message)
    {
        var callId = message["tool_call_id"]?.ToString();
        if (callId is null)
        {
            return [];
        }
        var call = Conversation.Messages.OfType<JsonNode>().SelectMany(Calls)
                    .FirstOrDefault(x => x["id"]?.ToString() == callId);
        var function = call?["function"]?["name"]?.ToString();
        if (function is not ("project_context" or "list_workflows" or "get_workflow" or "save_workflow"
            or "list_jobs" or "get_job" or "save_job"))
        {
            return [];
        }
        try
        {
            var value = JsonNode.Parse(Text(message));
            var items = new List<WorkspaceItem>();
            void Add(JsonNode? node, string kind)
            {
                if (node is JsonArray array)
                {
                    foreach (var entry in array)
                    {
                        Add(entry, kind);
                    }
                    return;
                }
                if (node is not JsonObject obj)
                {
                    return;
                }
                var id = obj.FirstOrDefault(x => x.Key.Equals("id", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                var name = obj.FirstOrDefault(x => x.Key.Equals("name", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                if (Guid.TryParse(id, out var parsed))
                {
                    items.Add(new(parsed, kind, name ?? (kind == "workflow" ? "Workflow" : "Job")));
                }
            }
            void ApplyToolContext()
            {
                if (function == "project_context" && value is JsonObject context)
                {
                    Add(context["Workflows"], "workflow");
                    Add(context["Jobs"], "job");
                    return;
                }
                Add(value, function.Contains("workflow", StringComparison.Ordinal) ? "workflow" : "job");
            }
            ApplyToolContext();
            return items.DistinctBy(x => (x.Kind, x.Id)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task OpenPreviewAsync(WorkspaceItem item)
    {
        if (item.Kind is not ("workflow" or "job"))
        {
            return;
        }
        OpenWorkspace();
        ShowOverview = false;
        PreviewChart = null;
        var version = ++_previewVersion;
        var projectId = Conversation.ProjectId;
        PreviewItem = item;
        PreviewWorkflow = null;
        PreviewJob = null;
        PreviewError = null;
        PreviewLoading = true;
        NotifyStateChanged();
        try
        {
            var permission = item.Kind == "workflow" ? Permission.ChainsManage : Permission.JobsView;
            if (!await permissions.HasAsync(permission))
            {
                throw new UnauthorizedAccessException();
            }
            async Task LoadWorkspaceItemAsync()
            {
                if (item.Kind == "workflow")
                {
                    var workflow = (await service.ListJobChainsAsync(projectId))
                        .FirstOrDefault(x => x.Id == item.Id && x.ProjectId == projectId)
                        ?? throw new InvalidOperationException();
                    if (version == _previewVersion)
                    {
                        PreviewWorkflow = workflow;
                        PreviewItem = item with
                        {
                            Name = workflow.Name
                        };
                    }
                    return;
                }
                {
                    var job = await service.GetJobAsync(item.Id);
                    if (job is null || job.ProjectId != projectId)
                    {
                        throw new InvalidOperationException();
                    }
                    if (version == _previewVersion)
                    {
                        PreviewJob = job;
                        PreviewItem = item with
                        {
                            Name = job.Name
                        };
                    }
                }
            }
            await LoadWorkspaceItemAsync();
        }
        catch (Exception)
        {
            if (version == _previewVersion)
            {
                PreviewError = "Could not open this item. It may have been removed or access is restricted.";
            }
        }
        finally
        {
            if (version == _previewVersion)
            {
                PreviewLoading = false;
            }
            NotifyStateChanged();
        }
    }
    public Task OpenJobPreviewAsync(Guid id) => OpenPreviewAsync(new(id, "job", "Job"));
    public void ClosePreview()
    {
        ++_previewVersion;
        PreviewChart = null;
        PreviewItem = null;
        PreviewWorkflow = null;
        PreviewJob = null;
        PreviewError = null;
        PreviewLoading = false;
    }
    private void ResetWorkspace()
    {
        CloseWorkspace();
        _workspaceVisible = null;
        _autoPreviewCall = null;
    }
    private async Task AutoPreviewAsync()
    {
        if (PreviewLoading || _workspaceVisible == false)
        {
            return;
        }
        foreach (var message in Conversation.Messages.OfType<JsonNode>().Reverse().Where(x => Role(x) == "tool"))
        {
            var chart = ResultChart(message);
            if (chart is not null)
            {
                var chartCall = message["tool_call_id"]?.ToString();
                if (chartCall == _autoPreviewCall)
                {
                    return;
                }
                _autoPreviewCall = chartCall;
                OpenChart(chart);
                return;
            }
            var items = ResultItems(message);
            if (items.Count == 0)
            {
                continue;
            }
            var call = message["tool_call_id"]?.ToString();
            if (call == _autoPreviewCall)
            {
                return;
            }
            _autoPreviewCall = call;
            await OpenPreviewAsync(items.FirstOrDefault(x => x.Kind == "workflow") ?? items[0]);
            return;
        }
    }
}
