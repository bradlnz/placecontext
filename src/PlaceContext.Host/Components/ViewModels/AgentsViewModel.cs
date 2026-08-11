using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentsViewModel(IPlaceContextService service, PortalUiState ui) : PageViewModel
{
    private static readonly TimeSpan WorkRefreshInterval = TimeSpan.FromSeconds(6);
    public Guid ProjectId { get; private set; }
    public bool Loading { get; private set; }
    public bool Saving { get; private set; }
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public IReadOnlyList<AgentDefinitionView> Agents { get; private set; } = [];
    public IReadOnlyList<JobView> Jobs { get; private set; } = [];
    public IReadOnlyList<AgentTemplateView> Templates => AgentTemplateCatalog.All;
    public IReadOnlyList<AgentCapability> CapabilityOptions { get; } = Enum.GetValues<AgentCapability>();
    public AgentDefinitionView? CommandAgent => Agents.FirstOrDefault(agent => agent.Kind == AgentKind.Command);
    public IEnumerable<AgentDefinitionView> Workers => Agents.Where(agent => agent.Kind == AgentKind.Worker);

    public IReadOnlyList<RunReportView> WorkItems { get; private set; } = [];
    private Timer? _workItemPoller;
    private bool _workItemRefreshing;
    public static IReadOnlyList<string> WorkBuckets { get; } = new[]
    {
        "queued",
        "running",
        "review",
        "done",
        "blocked",
    };

    public bool EditorOpen { get; private set; }
    public bool WorkItemOpen => SelectedWorkItem is not null;
    public RunReportView? SelectedWorkItem { get; private set; }
    public bool WorkItemComposerOpen { get; private set; }
    public bool WorkItemComposerSubmitting { get; private set; }
    public Guid? WorkComposeJobId { get; private set; }
    public string WorkComposeGoal { get; set; } = string.Empty;
    public Guid? EditId { get; private set; }
    public AgentKind EditKind { get; private set; } = AgentKind.Worker;
    public string EditName { get; set; } = string.Empty;
    public string EditDescription { get; set; } = string.Empty;
    public string EditInstructions { get; set; } = string.Empty;
    public string EditTemplateKey { get; private set; } = string.Empty;
    public bool EditEnabled { get; set; } = true;
    public Guid? EditParentAgentId { get; set; }
    public HashSet<AgentCapability> EditCapabilities { get; } = [];
    public HashSet<Guid> EditAllowedJobs { get; } = [];
    public bool EditingCommand => EditKind == AgentKind.Command;
    public IEnumerable<AgentDefinitionView> ParentCandidates => Agents
        .Where(agent => agent.Kind == AgentKind.Command || (agent.Kind == AgentKind.Worker && agent.Id != EditId));

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        ui.Set("Agents", "Command Agent orchestration");
        Loading = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            await service.EnsureCommandAgentAsync(projectId);
            var agentsTask = service.ListAgentDefinitionsAsync(projectId);
            var jobsTask = service.ListJobsAsync(projectId);
            var workTask = service.ListRecentRunReportsAsync(48);
            await Task.WhenAll(agentsTask, jobsTask, workTask);
            Agents = await agentsTask;
            Jobs = await jobsTask;
            WorkItems = await workTask;
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public void Edit(AgentDefinitionView agent)
    {
        EditId = agent.Id;
        EditKind = agent.Kind;
        EditName = agent.Name;
        EditDescription = agent.Description;
        EditInstructions = agent.Instructions;
        EditTemplateKey = agent.TemplateKey;
        EditEnabled = agent.Enabled;
        EditParentAgentId = agent.ParentAgentId;
        EditCapabilities.Clear();
        EditCapabilities.UnionWith(agent.Capabilities);
        EditAllowedJobs.Clear();
        EditAllowedJobs.UnionWith(agent.AllowedJobIds);
        EditorOpen = true;
        Message = null;
        NotifyStateChanged();
    }

    public void OpenWorkItemComposer()
    {
        if (Jobs.Count == 0)
        {
            SetMessage("Create a job before adding a work item.", true);
            NotifyStateChanged();
            return;
        }

        EditorOpen = false;
        WorkItemComposerOpen = true;
        SelectedWorkItem = null;
        WorkComposeGoal = string.Empty;
        WorkItemComposerSubmitting = false;

        var selectedJob = Jobs.OrderBy(j => j.Name).FirstOrDefault();
        if (selectedJob is not null)
            SetWorkComposeJob(selectedJob.Id.ToString());
        else
            WorkComposeJobId = null;

        Message = null;
        NotifyStateChanged();
    }

    public void CloseWorkItemComposer()
    {
        WorkItemComposerOpen = false;
        WorkComposeJobId = null;
        WorkComposeGoal = string.Empty;
        WorkItemComposerSubmitting = false;
        NotifyStateChanged();
    }

    public void SetWorkComposeJob(string? value)
    {
        if (!Guid.TryParse(value, out var jobId))
        {
            WorkComposeJobId = null;
            return;
        }

        var job = Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null)
        {
            WorkComposeJobId = null;
            return;
        }

        WorkComposeJobId = jobId;
        WorkItemComposerSubmitting = false;
        NotifyStateChanged();
    }

    public JobView? WorkComposeJob => WorkComposeJobId is { } id
        ? Jobs.FirstOrDefault(job => job.Id == id)
        : Jobs.FirstOrDefault();

    public RunReportView[] WorkItemsFor(string bucket) =>
        [..WorkItems
            .Where(i => WorkBucket(i.Run.Status) == bucket)
            .OrderByDescending(i => i.Run.StartedAt)];

    public int WorkItemCount(string bucket) => WorkItemsFor(bucket).Length;

    public static string WorkBucketLabel(string bucket) => bucket switch
    {
        "queued" => "Queued",
        "running" => "Running",
        "review" => "Review",
        "done" => "Done",
        "blocked" => "Blocked",
        _ => bucket,
    };

    public static string WorkBucketClass(string status) => WorkBucket(status) switch
    {
        "queued" => "work-status-queued",
        "running" => "work-status-running",
        "review" => "work-status-review",
        "done" => "work-status-done",
        "blocked" => "work-status-blocked",
        _ => "work-status-review",
    };

    public void OpenWorkItem(RunReportView item)
    {
        SelectedWorkItem = item;
        Message = null;
        NotifyStateChanged();
    }

    public async Task CreateWorkItemAsync()
    {
        var job = WorkComposeJob;
        if (job is null)
        {
            SetMessage("Select a job first.", true);
            WorkItemComposerSubmitting = false;
            NotifyStateChanged();
            return;
        }

        if (WorkItemComposerSubmitting)
            return;

        var goal = WorkComposeGoal?.Trim();
        if (string.IsNullOrWhiteSpace(goal))
        {
            SetMessage("Goal is required.", true);
            NotifyStateChanged();
            return;
        }
        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["goal"] = goal });

        WorkItemComposerSubmitting = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            await service.RunJobAsync(job.Id, payload);
            CloseWorkItemComposer();
            await RefreshWorkItemsAsync(silent: false);
            SetMessage("Work item added.", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
            WorkItemComposerSubmitting = false;
            NotifyStateChanged();
        }
    }

    public void CloseWorkItem()
    {
        SelectedWorkItem = null;
        NotifyStateChanged();
    }

    public static string WorkSummaryTitle(RunReportView item) =>
        string.IsNullOrWhiteSpace(item.JobName) ? "Untitled work" : item.JobName;

    public static string WorkSummaryProject(RunReportView item) =>
        string.IsNullOrWhiteSpace(item.ProjectName) ? "—" : item.ProjectName;

    public static string WorkGoal(RunReportView item) =>
        string.IsNullOrWhiteSpace(item.Run.Snapshot.Goal) ? "No goal provided" : item.Run.Snapshot.Goal;

    public static string WorkStartedAt(RunReportView item) =>
        item.Run.StartedAt.ToLocalTime().ToString("MMM d, yyyy • HH:mm");

    public static string WorkFinishedAt(RunReportView item) =>
        item.Run.FinishedAt?.ToLocalTime().ToString("MMM d, yyyy • HH:mm") ?? "Running";

    public static string WorkDuration(RunReportView item)
    {
        if (item.Run.FinishedAt is null)
            return "running";

        return FormatHelper.Duration(item.Run.StartedAt, item.Run.FinishedAt.Value);
    }

    public static int WorkShardCount(RunReportView item) => item.Run.ShardResults.Count;

    public static int WorkShardSuccessCount(RunReportView item) =>
        item.Run.ShardResults.Count(shard => shard.ExitCode == 0);

    public static int WorkShardFailureCount(RunReportView item) =>
        item.Run.ShardResults.Count(shard => shard.ExitCode != 0);

    public static string WorkAttemptLabel(RunReportView item) =>
        $"Attempt {item.Run.AttemptNumber}";

    public static string WorkStatusLabel(string status) =>
        string.IsNullOrWhiteSpace(status) ? "Review" : status;

    public string ParentAgentLabel(Guid? parentAgentId)
    {
        if (parentAgentId is null)
            return "Command root";

        var parent = Agents.FirstOrDefault(agent => agent.Id == parentAgentId);
        return parent is null ? "Command root" : parent.Name;
    }

    public void StartWorkItemPolling()
    {
        if (_workItemPoller is not null)
            return;

        _workItemPoller = new Timer(
            _ => _ = RefreshWorkItemsAsync(silent: true),
            null,
            WorkRefreshInterval,
            WorkRefreshInterval
        );
    }

    public void StopWorkItemPolling()
    {
        var poller = _workItemPoller;
        if (poller is null)
            return;
        poller.Dispose();
        _workItemPoller = null;
    }

    public async Task RefreshWorkItemsAsync(bool silent = false)
    {
        if (_workItemRefreshing)
            return;

        _workItemRefreshing = true;
        try
        {
            var workItems = await service.ListRecentRunReportsAsync(48);
            WorkItems = workItems;

            if (SelectedWorkItem is { } selectedWorkItem)
            {
                var updated = WorkItems.FirstOrDefault(item =>
                    item.Run.Id == selectedWorkItem.Run.Id
                );
                SelectedWorkItem = updated;
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                SetMessage(ex.Message, true);
        }
        finally
        {
            _workItemRefreshing = false;
            NotifyStateChanged();
        }
    }

    public static string WorkBucket(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "review";

        if (status.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            return "blocked";

        if (status.Equals("Partial", StringComparison.OrdinalIgnoreCase))
            return "review";

        return ScopedPresentationCatalog.JobStatus(status) switch
        {
            JobRunStatus.Queued => "queued",
            JobRunStatus.Running => "running",
            JobRunStatus.Succeeded => "done",
            JobRunStatus.Failed => "blocked",
            JobRunStatus.Partial => "review",
            _ => "review",
        };
    }

    public void CreateFromTemplate(AgentTemplateView template)
    {
        EditId = null;
        EditKind = AgentKind.Worker;
        EditName = template.Name;
        EditDescription = template.Description;
        EditInstructions = template.Instructions;
        EditTemplateKey = template.Key;
        EditParentAgentId = CommandAgent?.Id;
        EditEnabled = true;
        EditCapabilities.Clear();
        EditCapabilities.UnionWith(template.Capabilities);
        EditAllowedJobs.Clear();
        EditorOpen = true;
        Message = null;
        NotifyStateChanged();
    }

    public void CreateBlankWorker()
    {
        EditId = null;
        EditKind = AgentKind.Worker;
        EditName = "New worker agent";
        EditDescription = string.Empty;
        EditInstructions = string.Empty;
        EditTemplateKey = string.Empty;
        EditParentAgentId = CommandAgent?.Id;
        EditEnabled = true;
        EditCapabilities.Clear();
        EditCapabilities.Add(AgentCapability.GraphRead);
        EditAllowedJobs.Clear();
        EditorOpen = true;
        Message = null;
        NotifyStateChanged();
    }

    public void ToggleCapability(AgentCapability capability)
    {
        if (EditingCommand || capability == AgentCapability.GraphRead)
            return;
        if (!EditCapabilities.Remove(capability))
            EditCapabilities.Add(capability);
    }

    public void ToggleJob(Guid jobId)
    {
        if (!EditAllowedJobs.Remove(jobId))
            EditAllowedJobs.Add(jobId);
    }

    public bool HasCapability(AgentCapability capability) => EditCapabilities.Contains(capability);
    public bool HasJob(Guid jobId) => EditAllowedJobs.Contains(jobId);

    public void SetEditParent(string? parentAgentId)
    {
        if (string.IsNullOrWhiteSpace(parentAgentId))
        {
            EditParentAgentId = null;
            return;
        }

        EditParentAgentId = Guid.TryParse(parentAgentId, out var parsed) ? parsed : null;
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            SetMessage("Agent name is required.", true);
            return;
        }

        Saving = true;
        NotifyStateChanged();
        try
        {
            await service.SaveAgentDefinitionAsync(new SaveAgentDefinitionCommand(
                ProjectId, EditId, EditName, EditDescription, EditInstructions, EditTemplateKey,
                EditCapabilities.ToArray(), EditAllowedJobs.ToArray(), EditParentAgentId, EditEnabled));
            EditorOpen = false;
            Agents = await service.ListAgentDefinitionsAsync(ProjectId);
            SetMessage("Agent saved.", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteAsync()
    {
        if (!EditId.HasValue || EditingCommand)
            return;
        try
        {
            await service.DeleteAgentDefinitionAsync(EditId.Value);
            EditorOpen = false;
            Agents = await service.ListAgentDefinitionsAsync(ProjectId);
            SetMessage("Agent removed.", false);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        EditorOpen = false;
        NotifyStateChanged();
    }

    public static string CapabilityLabel(AgentCapability capability) => capability switch
    {
        AgentCapability.GraphRead => "Read data graph",
        AgentCapability.DataRead => "Read project data",
        AgentCapability.ArtifactsRead => "Read artifacts",
        AgentCapability.JobsRead => "Inspect jobs",
        AgentCapability.JobsRun => "Run jobs",
        AgentCapability.ChainsRead => "Inspect chains",
        AgentCapability.ChainsRun => "Run chains",
        AgentCapability.SchedulesRead => "Inspect schedules",
        AgentCapability.SchedulesManage => "Manage schedules",
        AgentCapability.McpCall => "Call MCP tools",
        _ => capability.ToString(),
    };

    private void SetMessage(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }
}
