using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class EventsViewModel : PageViewModel
{
    private readonly PlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;

    public EventsViewModel(
        PlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation
    ) => (_service, _ui, _navigation) = (service, ui, navigation);

    public Guid ProjectId
    {
        get; private set;
    }
    public IReadOnlyList<EventTypeView>? Types
    {
        get; private set;
    }
    public IReadOnlyList<EventOccurrenceView>? Log
    {
        get; private set;
    }
    public IReadOnlyList<TriggerView>? Triggers
    {
        get; private set;
    }
    public bool Loading { get; private set; } = true;
    public string? Message
    {
        get; private set;
    }
    public bool MessageIsError
    {
        get; private set;
    }
    public bool ShowDefinition
    {
        get; private set;
    }
    public string DefinitionName { get; set; } = "";
    public string DefinitionDescription { get; set; } = "";
    public string DefinitionSchema { get; set; } = "";
    public bool DefinitionBusy
    {
        get; private set;
    }
    public string? DefinitionError
    {
        get; private set;
    }
    public string? EmitName
    {
        get; private set;
    }
    public string EmitPayload { get; set; } = "";
    public bool EmitBusy
    {
        get; private set;
    }
    public string? EmitError
    {
        get; private set;
    }
    public string Tab { get; set; } = "activity";
    public bool IsActivityTab => Tab == "activity";
    public bool IsTypesTab => Tab == "types";
    public bool IsSubscriptionsTab => Tab == "subscriptions";
    public void SelectActivityTab() => Tab = "activity";
    public void SelectTypesTab() => Tab = "types";
    public void SelectSubscriptionsTab() => Tab = "subscriptions";
    public string Search { get; set; } = "";
    public string Source { get; set; } = "";
    public string Scope { get; set; } = "project";
    public string SubscriptionEvent { get; set; } = "";
    public IEnumerable<EventOccurrenceView> FilteredLog => (Log ?? []).Where(item =>
        MatchesActivity(item, ProjectId, Scope, Source, Search));
    public IEnumerable<TriggerView> EventSubscriptions => (Triggers ?? []).Where(trigger =>
        trigger.Kind == TriggerKinds.Event && (SubscriptionEvent.Length == 0
            || string.Equals(trigger.EventName, SubscriptionEvent, StringComparison.OrdinalIgnoreCase)));
    public int CustomTypeCount => Types?.Count(type => !type.IsBuiltIn) ?? 0;
    public int ActiveSubscriptionCount =>
        Triggers?.Count(trigger => trigger.Enabled && trigger.Kind == TriggerKinds.Event) ?? 0;
    public int SubscribedTypeCount => Types?.Count(type => SubscriberCount(type.Name) > 0) ?? 0;
    public int SubscriptionPercent =>
        Types is not { Count: > 0 } ? 0 : (int)Math.Round(SubscribedTypeCount * 100d / Types.Count);

    private static class TriggerKinds
    {
        public const string Event = "Event";
    }

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        Loading = true;
        Message = null;
        MessageIsError = false;
        try
        {
            await ReloadAsync();
            _ui.Set("Events", "activity, event types, and job subscriptions");
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public async Task ReloadAsync()
    {
        Types = await _service.ListEventTypesAsync();
        Log = await _service.ListEventOccurrencesAsync(100);
        Triggers = await _service.ListTriggersAsync(ProjectId);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await ReloadAsync();
            Message = null;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            MessageIsError = true;
        }
        NotifyStateChanged();
    }

    public void ShowSubscriptions(string name)
    {
        SubscriptionEvent = name;
        Tab = "subscriptions";
    }

    public static bool MatchesActivity(EventOccurrenceView item, Guid projectId, string scope, string source, string search) =>
        (scope == "workspace" || item.ProjectId == projectId)
        && (source.Length == 0 || string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase))
        && (search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Payload?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    public static Guid? OriginRunId(EventOccurrenceView occurrence)
    {
        if (occurrence.Name != BuiltInEvents.JobCompleted || occurrence.Source != "Domain")
        {
            return null;
        }
        return Guid.TryParse(PayloadField(occurrence.Payload, "runId"), out var id) ? id : null;
    }

    public static string? Outcome(EventOccurrenceView occurrence) => occurrence.Name == BuiltInEvents.JobCompleted
        ? PayloadField(occurrence.Payload, "status") : null;

    private static string? PayloadField(string? payload, string field)
    {
        try
        {
            using var document = JsonDocument.Parse(payload ?? "{}");
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public int SubscriberCount(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? 0
            : Triggers?.Count(trigger =>
                trigger.Enabled
                && trigger.Kind == TriggerKinds.Event
                && string.Equals(trigger.EventName, name, StringComparison.OrdinalIgnoreCase)
            )
                ?? 0;

    public static string SourceClass(string source) =>
        string.Equals(source, EventSources.Domain, StringComparison.OrdinalIgnoreCase)
            ? "domain"
            : "user";

    public static string SourceLabel(string source) =>
        string.Equals(source, EventSources.Domain, StringComparison.OrdinalIgnoreCase)
            ? "system"
            : "manual";

    private static class EventSources
    {
        public const string Domain = "Domain";
    }

    public void BeginDefine()
    {
        DefinitionName = DefinitionDescription = DefinitionSchema = "";
        DefinitionError = null;
        ShowDefinition = true;
    }

    public void CloseDefinition()
    {
        if (!DefinitionBusy)
        {
            ShowDefinition = false;
        }
    }

    public async Task DefineAsync()
    {
        DefinitionError = null;
        if (string.IsNullOrWhiteSpace(DefinitionName))
        {
            DefinitionError = "Name is required.";
            return;
        }
        DefinitionBusy = true;
        try
        {
            var name = DefinitionName.Trim();
            await _service.DefineEventTypeAsync(
                name,
                string.IsNullOrWhiteSpace(DefinitionDescription)
                    ? null
                    : DefinitionDescription.Trim(),
                string.IsNullOrWhiteSpace(DefinitionSchema) ? null : DefinitionSchema.Trim()
            );
            await ReloadAsync();
            ShowDefinition = false;
            Message = $"Created event type '{name}'.";
            MessageIsError = false;
        }
        catch (Exception ex)
        {
            DefinitionError = ex.Message;
        }
        finally
        {
            DefinitionBusy = false;
            NotifyStateChanged();
        }
    }

    public void Emit(string name)
    {
        EmitName = name;
        EmitPayload = "";
        EmitError = null;
    }

    public void CloseEmit()
    {
        if (!EmitBusy)
        {
            EmitName = null;
        }
    }

    public async Task EmitAsync()
    {
        if (EmitName is null)
        {
            return;
        }
        EmitError = null;
        EmitBusy = true;
        try
        {
            var name = EmitName;
            var result = await _service.EmitEventAsync(
                name,
                ProjectId,
                string.IsNullOrWhiteSpace(EmitPayload) ? null : EmitPayload
            );
            await ReloadAsync();
            EmitName = null;
            Message = $"Emitted '{name}' — {result.TriggeredRuns} trigger(s) fired.";
            MessageIsError = false;
        }
        catch (Exception ex)
        {
            EmitError = ex.Message;
        }
        finally
        {
            EmitBusy = false;
            NotifyStateChanged();
        }
    }

    public void OpenJobs() => _navigation.NavigateTo($"/project/{ProjectId}/jobs");
    public void OpenSubscription(TriggerView trigger) =>
        _navigation.NavigateTo($"/project/{trigger.ProjectId}/jobs?job={trigger.JobId}&tab=triggers");
}
