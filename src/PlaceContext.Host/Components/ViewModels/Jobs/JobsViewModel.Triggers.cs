using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobsViewModel
{
    // ── Triggers state ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<TriggerView>? Triggers { get; private set; }
    public IReadOnlyList<EventTypeView>? EventTypes { get; private set; }
    public string TrName { get; set; } = "";
    public string TrKind { get; set; } = "Schedule";
    public string TrCron { get; set; } = "0 0 * * *";
    public string TrEvent { get; set; } = "";
    public bool TrBusy { get; private set; }
    public string? TrError { get; private set; }

    // ── Triggers ──────────────────────────────────────────────────────────────────────────────
    public IEnumerable<TriggerView> JobTriggers() =>
        Triggers?.Where(t => t.JobId == SelectedJobId) ?? Enumerable.Empty<TriggerView>();

    public async Task AddTriggerAsync()
    {
        TrError = null;
        if (!SelectedJobId.HasValue)
            return;
        if (string.IsNullOrWhiteSpace(TrName))
        {
            TrError = "Name is required.";
            NotifyStateChanged();
            return;
        }

        TrBusy = true;
        try
        {
            var cron = TrKind == "Schedule" ? TrCron : null;
            var evt = TrKind == "Event" ? TrEvent : null;
            await _svc.CreateTriggerAsync(
                new CreateTriggerCommand(SelectedJobId.Value, TrName.Trim(), TrKind, cron, evt)
            );
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            TrName = "";
            TrEvent = "";
        }
        catch (Exception ex)
        {
            TrError = ex.Message;
        }
        finally
        {
            TrBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task ToggleTriggerAsync(TriggerView t)
    {
        try
        {
            await _svc.SetTriggerEnabledAsync(t.Id, !t.Enabled);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            TrError = ex.Message;
            NotifyStateChanged();
        }
    }

    public async Task RemoveTriggerAsync(Guid triggerId)
    {
        try
        {
            await _svc.DeleteTriggerAsync(triggerId);
            Triggers = await _svc.ListTriggersAsync(ProjectId);
            if (SelectedTriggerId == triggerId)
                SelectedTriggerId = null;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            TrError = ex.Message;
            NotifyStateChanged();
        }
    }

    public TriggerView? SelectedTrigger() =>
        SelectedTriggerId is { } id ? JobTriggers().FirstOrDefault(t => t.Id == id) : null;
}
