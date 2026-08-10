using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentsViewModel(IPlaceContextService service, PortalUiState ui) : PageViewModel
{
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

    public bool EditorOpen { get; private set; }
    public Guid? EditId { get; private set; }
    public AgentKind EditKind { get; private set; } = AgentKind.Worker;
    public string EditName { get; set; } = string.Empty;
    public string EditDescription { get; set; } = string.Empty;
    public string EditInstructions { get; set; } = string.Empty;
    public string EditTemplateKey { get; private set; } = string.Empty;
    public bool EditEnabled { get; set; } = true;
    public HashSet<AgentCapability> EditCapabilities { get; } = [];
    public HashSet<Guid> EditAllowedJobs { get; } = [];
    public bool EditingCommand => EditKind == AgentKind.Command;

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
            await Task.WhenAll(agentsTask, jobsTask);
            Agents = await agentsTask;
            Jobs = await jobsTask;
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
        EditCapabilities.Clear();
        EditCapabilities.UnionWith(agent.Capabilities);
        EditAllowedJobs.Clear();
        EditAllowedJobs.UnionWith(agent.AllowedJobIds);
        EditorOpen = true;
        Message = null;
        NotifyStateChanged();
    }

    public void CreateFromTemplate(AgentTemplateView template)
    {
        EditId = null;
        EditKind = AgentKind.Worker;
        EditName = template.Name;
        EditDescription = template.Description;
        EditInstructions = template.Instructions;
        EditTemplateKey = template.Key;
        EditEnabled = true;
        EditCapabilities.Clear();
        EditCapabilities.UnionWith(template.Capabilities);
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
                EditCapabilities.ToArray(), EditAllowedJobs.ToArray(), EditEnabled));
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
