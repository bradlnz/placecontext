using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectViewModel : PageViewModel
{
    private readonly PlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;

    public ProjectViewModel(
        PlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation,
        IJSRuntime js
    ) => (_service, _ui, _navigation, _js) = (service, ui, navigation, js);

    public static class Tabs
    {
        public const string Overview = "overview";
        public const string Requirements = "requirements";
        public const string Activity = "activity";
    }

    public static IReadOnlyList<(string Key, string Label)> TabItems
    {
        get;
    } =
    [(Tabs.Overview, "Overview"), (Tabs.Requirements, "Requirements"), (Tabs.Activity, "Activity")];

    public static string NormalizeTab(string? tab) =>
        tab is Tabs.Requirements or Tabs.Activity ? tab : Tabs.Overview;

    public Guid Id
    {
        get; private set;
    }
    public ProjectOverviewView? Overview
    {
        get; private set;
    }
    public ActivityTimelineView? Timeline
    {
        get; private set;
    }
    public IReadOnlyList<DecisionView>? Decisions
    {
        get; private set;
    }
    public string Tab { get; private set; } = Tabs.Overview;
    public string? Message
    {
        get; private set;
    }
    public RequirementsView? Requirements
    {
        get; private set;
    }
    public string RequirementsDraft { get; set; } = "";
    public string RequirementsSaved { get; private set; } = "";
    public bool SavingRequirements
    {
        get; private set;
    }
    public bool RequirementsDirty =>
        !string.Equals(RequirementsDraft, RequirementsSaved, StringComparison.Ordinal);
    public bool ScrollPending
    {
        get; private set;
    }
    private int _loadVersion;

    public bool HasDecisionRationale(DecisionView decision) =>
        !string.IsNullOrWhiteSpace(decision.Rationale) && decision.Rationale != "(none)";

    public string RequirementUpdated(DateTimeOffset value) => Presentation.DateTime(value);

    public string DecisionDate(DateTimeOffset value) => Presentation.Date(value);

    public string ChangeColor(string kind) => Presentation.StatusColor(kind);

    public string ChangeBackground(string kind) => Presentation.StatusBackground(kind);

    public string ChangeLabel(string kind) => kind.ToLowerInvariant();

    public async Task LoadAsync(Guid id)
    {
        var version = ++_loadVersion;
        Overview = null;
        Timeline = null;
        Decisions = null;
        Requirements = null;
        RequirementsDraft = RequirementsSaved = "";
        Message = null;
        Id = id;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(_navigation.Uri).Query);
        Tab = NormalizeTab(query["tab"]);
        await LoadOverviewAsync();
        if (version != _loadVersion)
        {
            return;
        }
        await LoadDetailsAsync();
    }

    private async Task LoadOverviewAsync()
    {
        var version = _loadVersion;
        var overview = await _service.GetProjectOverviewAsync(Id);
        if (version != _loadVersion)
        {
            return;
        }
        Overview = overview;
        _ui.Set(Overview.Name, $"{Overview.Path} · {Overview.Status}");
        ScrollPending = true;
        NotifyStateChanged();
    }

    private async Task LoadDetailsAsync()
    {
        var version = _loadVersion;
        if (Tab == Tabs.Requirements && Requirements is null)
        {
            await LoadRequirementsAsync();
        }
        if (Tab == Tabs.Activity)
        {
            if (Timeline is null)
            {
                await LoadTimelineAsync();
            }
            if (version != _loadVersion)
            {
                return;
            }
            if (Decisions is null)
            {
                await LoadDecisionsAsync();
            }
        }
        NotifyStateChanged();
    }

    private async Task LoadTimelineAsync()
    {
        var version = _loadVersion;
        try
        {
            var timeline = await _service.GetTimelineAsync(Id, 8);
            if (version == _loadVersion)
            {
                Timeline = timeline;
            }
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                Message = $"Could not load timeline: {ex.Message}";
            }
        }
    }

    private async Task LoadRequirementsAsync()
    {
        var version = _loadVersion;
        try
        {
            var requirements = await _service.GetProjectRequirementsAsync(Id);
            if (version != _loadVersion)
            {
                return;
            }
            Requirements = requirements;
            RequirementsDraft = RequirementsSaved = Requirements.Markdown;
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                Message = $"Could not load requirements: {ex.Message}";
            }
        }
    }

    private async Task LoadDecisionsAsync()
    {
        var version = _loadVersion;
        try
        {
            var decisions = await _service.GetDecisionsAsync(Id);
            if (version == _loadVersion)
            {
                Decisions = decisions;
            }
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                Message = $"Could not load decisions: {ex.Message}";
            }
        }
    }

    public async Task SwitchTab(string tab)
    {
        Tab = NormalizeTab(tab);
        NotifyStateChanged();
        await LoadDetailsAsync();
    }

    public async Task SaveRequirementsAsync()
    {
        SavingRequirements = true;
        Message = null;
        try
        {
            Requirements = await _service.SetProjectRequirementsAsync(Id, RequirementsDraft);
            RequirementsDraft = RequirementsSaved = Requirements.Markdown;
            Message = "Project requirements saved.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            SavingRequirements = false;
            NotifyStateChanged();
        }
    }

    public async Task AfterRenderAsync()
    {
        if (!ScrollPending || Overview is null)
        {
            return;
        }
        ScrollPending = false;
        await _js.InvokeVoidAsync("placecontext.scrollToHash");
    }

    public void BackToProjects() => _navigation.NavigateTo("/");

    public static string StatusColor(string kind) => Viz.AuthorColor(kind);

    public static string StatusBackground(string kind) => Viz.AuthorBg(kind);

    public static string LanguageColor(string path) => Viz.LangColor(path);
}
