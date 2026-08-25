using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentsSettingsViewModel(
    IPlaceContextService service,
    PortalUiState ui,
    NavigationManager navigation
) : PageViewModel
{
    public IReadOnlyList<ProjectSummaryView> Projects { get; private set; } = [];
    public IReadOnlyList<AgentDefinitionView> Team { get; private set; } = [];
    public Guid? ProjectId { get; private set; }
    public bool Loading { get; private set; } = true;
    public bool Saving { get; private set; }
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }

    public string BaseModel { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string Preamble { get; set; } = "";
    public string ToolCatalog { get; set; } = "";
    public string LaunchpadToolCatalog { get; set; } = "";
    public int MaxContextChunks { get; set; }
    public float Temperature { get; set; }
    public float TopP { get; set; }
    public bool Enabled { get; set; }

    public int EnabledAgentCount => Team.Count(agent => agent.Enabled);
    public string TeamHref => ProjectId is { } id ? PageRoutes.ProjectAgents(id) : "#";

    public async Task LoadAsync()
    {
        ui.Set("Settings", "Agents");
        Loading = true;
        NotifyStateChanged();
        try
        {
            Projects = await service.GetProjectsAsync();
            ProjectId = ui.CurrentProjectId is { } selected
                && Projects.Any(project => project.Id == selected)
                    ? selected
                    : Projects.FirstOrDefault()?.Id;
            await LoadProjectAsync();
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

    public async Task ProjectChangedAsync(ChangeEventArgs args)
    {
        ProjectId = Guid.TryParse(args.Value?.ToString(), out var id) ? id : null;
        Message = null;
        Loading = true;
        NotifyStateChanged();
        try
        {
            await LoadProjectAsync();
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

    public async Task SaveAsync()
    {
        if (ProjectId is null || Saving)
            return;

        if (string.IsNullOrWhiteSpace(BaseModel))
        {
            SetMessage("Base model is required.", true);
            NotifyStateChanged();
            return;
        }

        Saving = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            var saved = await service.UpdateAgentConfigAsync(
                new UpdateAgentConfigCommand(
                    ProjectId.Value,
                    BaseModel,
                    SystemPrompt,
                    Preamble,
                    ToolCatalog,
                    LaunchpadToolCatalog,
                    Math.Max(1, MaxContextChunks),
                    Math.Clamp(Temperature, 0f, 2f),
                    Math.Clamp(TopP, 0f, 1f),
                    Enabled
                )
            );
            Apply(saved);
            SetMessage("Agent settings saved.", false);
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

    public void OpenTeam()
    {
        if (ProjectId is { } id)
            navigation.NavigateTo(PageRoutes.ProjectAgents(id));
    }

    private async Task LoadProjectAsync()
    {
        if (ProjectId is null)
        {
            Team = [];
            return;
        }

        Apply(await service.GetAgentConfigAsync(ProjectId.Value));
        Team = await service.ListAgentDefinitionsAsync(ProjectId.Value);
    }

    private void Apply(AgentConfigView config)
    {
        BaseModel = config.BaseModel;
        SystemPrompt = config.SystemPrompt;
        Preamble = config.Preamble;
        ToolCatalog = config.ToolCatalog;
        LaunchpadToolCatalog = config.LaunchpadToolCatalog;
        MaxContextChunks = config.MaxContextChunks;
        Temperature = config.Temperature;
        TopP = config.TopP;
        Enabled = config.Enabled;
    }

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }
}
