using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class EventsViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;

    public EventsViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation
    ) => (_service, _ui, _navigation) = (service, ui, navigation);

    public Guid ProjectId { get; private set; }
    public IReadOnlyList<EventTypeView>? Types { get; private set; }
    public IReadOnlyList<EventOccurrenceView>? Log { get; private set; }
    public IReadOnlyList<TriggerView>? Triggers { get; private set; }
    public bool Loading { get; private set; } = true;
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public bool ShowDefinition { get; private set; }
    public string DefinitionName { get; set; } = "";
    public string DefinitionDescription { get; set; } = "";
    public string DefinitionSchema { get; set; } = "";
    public bool DefinitionBusy { get; private set; }
    public string? DefinitionError { get; private set; }
    public string? EmitName { get; private set; }
    public string EmitPayload { get; set; } = "";
    public bool EmitBusy { get; private set; }
    public string? EmitError { get; private set; }
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
            _ui.Set("Events", "workspace activity and event types");
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
        var types = _service.ListEventTypesAsync();
        var log = _service.ListEventOccurrencesAsync(50);
        var triggers = _service.ListTriggersAsync(ProjectId);
        await Task.WhenAll(types, log, triggers);
        Types = await types;
        Log = await log;
        Triggers = await triggers;
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
            ShowDefinition = false;
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
            EmitName = null;
    }

    public async Task EmitAsync()
    {
        if (EmitName is null)
            return;
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
}
