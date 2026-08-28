using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

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

    public static IReadOnlyList<(string Key, string Label)> TabItems { get; } =
    [(Tabs.Overview, "Overview"), (Tabs.Requirements, "Requirements"), (Tabs.Activity, "Activity")];

    public static string NormalizeTab(string? tab) =>
        tab is Tabs.Requirements or Tabs.Activity ? tab : Tabs.Overview;

    public Guid Id { get; private set; }
    public ProjectOverviewView? Overview { get; private set; }
    public ActivityTimelineView? Timeline { get; private set; }
    public IReadOnlyList<DecisionView>? Decisions { get; private set; }
    public string Tab { get; private set; } = Tabs.Overview;
    public string? Message { get; private set; }
    public RequirementsView? Requirements { get; private set; }
    public string RequirementsDraft { get; set; } = "";
    public string RequirementsSaved { get; private set; } = "";
    public bool SavingRequirements { get; private set; }
    public bool RequirementsDirty =>
        !string.Equals(RequirementsDraft, RequirementsSaved, StringComparison.Ordinal);
    public bool ScrollPending { get; private set; }

    public bool HasDecisionRationale(DecisionView decision) =>
        !string.IsNullOrWhiteSpace(decision.Rationale) && decision.Rationale != "(none)";

    public string RequirementUpdated(DateTimeOffset value) => Presentation.DateTime(value);

    public string DecisionDate(DateTimeOffset value) => Presentation.Date(value);

    public string ChangeColor(string kind) => Presentation.StatusColor(kind);

    public string ChangeBackground(string kind) => Presentation.StatusBackground(kind);

    public string ChangeLabel(string kind) => kind.ToLowerInvariant();

    public async Task LoadAsync(Guid id)
    {
        Id = id;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(_navigation.Uri).Query);
        Tab = NormalizeTab(query["tab"]);
        await LoadOverviewAsync();
        _ = LoadDetailsAsync();
    }

    private async Task LoadOverviewAsync()
    {
        Overview = await _service.GetProjectOverviewAsync(Id);
        _ui.Set(Overview.Name, $"{Overview.Path} · {Overview.Status}");
        ScrollPending = true;
        NotifyStateChanged();
    }

    private async Task LoadDetailsAsync()
    {
        var timeline = LoadTimelineAsync();
        var requirements = LoadRequirementsAsync();
        var decisions = LoadDecisionsAsync();
        await Task.WhenAll(timeline, requirements, decisions);
        NotifyStateChanged();
    }

    private async Task LoadTimelineAsync()
    {
        try
        {
            Timeline = await _service.GetTimelineAsync(Id, 8);
        }
        catch (Exception ex)
        {
            Message = $"Could not load timeline: {ex.Message}";
        }
    }

    private async Task LoadRequirementsAsync()
    {
        try
        {
            Requirements = await _service.GetProjectRequirementsAsync(Id);
            RequirementsDraft = RequirementsSaved = Requirements.Markdown;
        }
        catch (Exception ex)
        {
            Message = $"Could not load requirements: {ex.Message}";
        }
    }

    private async Task LoadDecisionsAsync()
    {
        try
        {
            Decisions = await _service.GetDecisionsAsync(Id);
        }
        catch (Exception ex)
        {
            Message = $"Could not load decisions: {ex.Message}";
        }
    }

    public void SwitchTab(string tab)
    {
        Tab = NormalizeTab(tab);
        NotifyStateChanged();
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
            return;
        ScrollPending = false;
        await _js.InvokeVoidAsync("placecontext.scrollToHash");
    }

    public void BackToProjects() => _navigation.NavigateTo("/");

    public static string StatusColor(string kind) => Viz.AuthorColor(kind);

    public static string StatusBackground(string kind) => Viz.AuthorBg(kind);

    public static string LanguageColor(string path) => Viz.LangColor(path);
}
