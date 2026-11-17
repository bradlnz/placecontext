using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class SchedulesViewModel : PageViewModel, IComponentViewModel
{
    private static class Copy
    {
        public const string PageTitle = "Schedules";
        public const string PageSubtitle = "scheduled workflow execution";
        public const string NameRequired = "Name is required.";
        public const string WorkflowRequired = "Pick a workflow.";
        public const string DeletedJob = "(deleted job)";
    }

    private static class Cron
    {
        public const string Hourly = "0 * * * *";
        public const string Default = "0 0 * * *";
        public const string Weekdays = "1-5";
    }

    private readonly PlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly ICurrentTenant _tenant;

    public SchedulesViewModel(PlaceContextService service, PortalUiState ui, ICurrentTenant tenant)
    {
        _service = service;
        _ui = ui;
        _tenant = tenant;
    }

    public static IReadOnlyList<TriggerKind> EditableTriggerKinds
    {
        get;
    } =
    [TriggerKind.Schedule, TriggerKind.Event];

    public IReadOnlyList<TriggerView>? Triggers
    {
        get; private set;
    }
    public IReadOnlyList<JobView>? Jobs
    {
        get; private set;
    }
    public IReadOnlyList<JobChainView>? Workflows { get; private set; }
    public IReadOnlyList<EventTypeView>? EventTypes
    {
        get; private set;
    }

    public string? Message
    {
        get; private set;
    }
    public string? Error
    {
        get; private set;
    }
    public bool Busy
    {
        get; private set;
    }
    public string TimeZoneId => _tenant.TimeZoneId;
    public bool ShowAddSchedule { get; private set; }

    public void OpenAddSchedule()
    {
        Error = null;
        ShowAddSchedule = true;
        NotifyStateChanged();
    }

    public void CloseAddSchedule()
    {
        if (Busy) return;
        ShowAddSchedule = false;
        Error = null;
        NotifyStateChanged();
    }

    public Guid NewWorkflowId
    {
        get; set;
    }
    public string NewName { get; set; } = string.Empty;
    public TriggerKind NewKind { get; set; } = TriggerKind.Schedule;
    public string NewCron { get; set; } = Cron.Default;
    public string NewEvent { get; set; } = string.Empty;

    public bool AdvancedCron
    {
        get; set;
    }
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Day;
    public int Weekday { get; set; } = (int)DayOfWeek.Monday;
    public int Day { get; set; } = 1;
    public TimeOnly Time { get; set; } = new(9, 0);

    public Guid? EditingId
    {
        get; private set;
    }
    public string? EditName
    {
        get; set;
    }
    public string? EditCron
    {
        get; set;
    }
    public string? EditEvent
    {
        get; set;
    }
    public string? EditError
    {
        get; private set;
    }
    public bool EditingBusy
    {
        get; private set;
    }

    public Guid? WorkflowId { get; private set; }
    public string? WorkflowName => Workflows?.FirstOrDefault(workflow => workflow.Id == WorkflowId)?.Name;

    private async Task RefreshTriggersAsync(Guid projectId)
    {
        var triggers = await _service.ListTriggersAsync(projectId);
        Triggers = WorkflowId is { } workflowId
            ? triggers.Where(trigger => trigger.WorkflowId == workflowId).ToArray()
            : triggers;
    }

    public async Task LoadAsync(Guid projectId, Guid? workflowId = null)
    {
        WorkflowId = workflowId;
        if (workflowId is { } selected) NewWorkflowId = selected;
        Message = null;
        try
        {
            Workflows = await _service.ListJobChainsAsync(projectId);
            Jobs = await _service.ListJobsAsync(projectId);
            await RefreshTriggersAsync(projectId);
            EventTypes = await _service.ListEventTypesAsync();
            NewEvent = EventTypes.FirstOrDefault()?.Name ?? string.Empty;
            _ui.Set(Copy.PageTitle, Copy.PageSubtitle);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    public void ToggleAdvancedCron()
    {
        AdvancedCron = !AdvancedCron;
        NotifyStateChanged();
    }

    public static string ComposeCron(
        ScheduleFrequency frequency,
        int weekday,
        int day,
        TimeOnly time
    ) =>
        frequency switch
        {
            ScheduleFrequency.Hour => Cron.Hourly,
            ScheduleFrequency.Weekday => $"{time.Minute} {time.Hour} * * {Cron.Weekdays}",
            ScheduleFrequency.Week => $"{time.Minute} {time.Hour} * * {weekday}",
            ScheduleFrequency.Month => $"{time.Minute} {time.Hour} {Math.Clamp(day, 1, 28)} * *",
            ScheduleFrequency.Day => $"{time.Minute} {time.Hour} * * *",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
        };

    public bool IsEvent(TriggerView trigger) => ParseKind(trigger.Kind) == TriggerKind.Event;

    public string NextRunLabel(TriggerView trigger) =>
        trigger.NextRunAt is { } value ? Presentation.ShortDateTime(value.ToWorkspaceTime()) : "—";

    public string LastFiredLabel(TriggerView trigger) =>
        trigger.LastFiredAt is { } value
            ? Presentation.ShortDateTime(value.ToWorkspaceTime())
            : "never";

    public string TargetLabel(TriggerView trigger) =>
        trigger.WorkflowId is { } workflowId ? Workflows?.FirstOrDefault(workflow => workflow.Id == workflowId)?.Name ?? "(deleted workflow)" : JobName(trigger.JobId ?? Guid.Empty);

    public async Task AddTriggerAsync(Guid projectId)
    {
        if (Busy) return;
        Error = null;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            Error = Copy.NameRequired;
            NotifyStateChanged();
            return;
        }

        Busy = true;
        try
        {
            var workflowId = WorkflowId ?? NewWorkflowId;
            if (workflowId == Guid.Empty || Workflows?.Any(workflow => workflow.Id == workflowId) != true)
            {
                Error = Copy.WorkflowRequired;
                return;
            }

            await _service.CreateTriggerAsync(
                new CreateTriggerCommand(
                    null,
                    NewName.Trim(),
                    "Schedule",
                    SelectedCron(),
                    null, workflowId
                )
            );

            await RefreshTriggersAsync(projectId);
            NewName = string.Empty;
            ShowAddSchedule = false;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public async Task ToggleAsync(Guid projectId, TriggerView trigger)
    {
        try
        {
            await _service.SetTriggerEnabledAsync(trigger.Id, !trigger.Enabled);
            await RefreshTriggersAsync(projectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    public async Task RemoveAsync(Guid projectId, Guid triggerId)
    {
        try
        {
            await _service.DeleteTriggerAsync(triggerId);
            await RefreshTriggersAsync(projectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    public void StartEdit(TriggerView trigger)
    {
        EditingId = trigger.Id;
        EditName = trigger.Name;
        EditCron = trigger.CronExpression;
        EditEvent = trigger.EventName;
        EditError = null;
        NotifyStateChanged();
    }

    public void CancelEdit()
    {
        EditingId = null;
        EditName = null;
        EditCron = null;
        EditEvent = null;
        EditError = null;
        NotifyStateChanged();
    }

    public async Task SaveEditAsync(Guid projectId)
    {
        EditError = null;
        if (string.IsNullOrWhiteSpace(EditName))
        {
            EditError = Copy.NameRequired;
            NotifyStateChanged();
            return;
        }

        EditingBusy = true;
        try
        {
            await _service.UpdateTriggerAsync(
                new UpdateTriggerCommand(
                    EditingId!.Value,
                    Name: EditName.Trim(),
                    CronExpression: EditCron?.Trim(),
                    EventName: EditEvent?.Trim(),
                    Enabled: null
                )
            );
            await RefreshTriggersAsync(projectId);
            CancelEdit();
        }
        catch (Exception ex)
        {
            EditError = ex.Message;
        }
        finally
        {
            EditingBusy = false;
            NotifyStateChanged();
        }
    }

    private string SelectedCron() =>
        AdvancedCron ? NewCron : ComposeCron(Frequency, Weekday, Day, Time);

    private string JobName(Guid jobId) =>
        Jobs?.FirstOrDefault(job => job.Id == jobId)?.Name ?? Copy.DeletedJob;

    private static TriggerKind ParseKind(string kind) =>
        Enum.TryParse<TriggerKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
}
