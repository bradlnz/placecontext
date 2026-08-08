using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class SchedulesViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly ICurrentTenant _tenant;

    public SchedulesViewModel(IPlaceContextService service, PortalUiState ui, ICurrentTenant tenant)
    {
        _service = service;
        _ui = ui;
        _tenant = tenant;
    }

    public static IReadOnlyList<TriggerKind> EditableTriggerKinds { get; } =
    [TriggerKind.Schedule, TriggerKind.Event, TriggerKind.Launchpad];

    public IReadOnlyList<TriggerView>? Triggers { get; private set; }
    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public IReadOnlyList<JobChainView>? Chains { get; private set; }
    public IReadOnlyList<ProjectTableInfo>? Tables { get; private set; }
    public IReadOnlyList<EventTypeView>? EventTypes { get; private set; }

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public string TimeZoneId => _tenant.TimeZoneId;

    public Guid NewJobId { get; set; }
    public string NewName { get; set; } = string.Empty;
    public TriggerKind NewKind { get; set; } = TriggerKind.Schedule;
    public string NewCron { get; set; } = ScheduleCron.Default;
    public string NewEvent { get; set; } = string.Empty;
    public Guid NewChainId { get; set; }
    public string NewSourceTable { get; set; } = string.Empty;
    public string NewPrompt { get; set; } = string.Empty;

    public bool AdvancedCron { get; set; }
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Day;
    public int Weekday { get; set; } = (int)DayOfWeek.Monday;
    public int Day { get; set; } = 1;
    public TimeOnly Time { get; set; } = new(9, 0);

    public Guid? EditingId { get; private set; }
    public string? EditName { get; set; }
    public string? EditCron { get; set; }
    public string? EditEvent { get; set; }
    public string? EditError { get; private set; }
    public bool EditingBusy { get; private set; }

    public async Task LoadAsync(Guid projectId)
    {
        try
        {
            Jobs = await _service.ListJobsAsync(projectId);
            Chains = await _service.ListJobChainsAsync(projectId);
            Tables = await _service.ListProjectDataTablesAsync(projectId);
            Triggers = await _service.ListTriggersAsync(projectId);
            EventTypes = await _service.ListEventTypesAsync();
            NewEvent = EventTypes.FirstOrDefault()?.Name ?? string.Empty;
            _ui.Set(ScheduleCopy.PageTitle, ScheduleCopy.PageSubtitle);
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

    public Task OnKindChangedAsync()
    {
        if (NewKind == TriggerKind.Launchpad && Chains?.Count > 0 && NewChainId == Guid.Empty)
            NewChainId = Chains[0].Id;

        return Task.CompletedTask;
    }

    public static string ComposeCron(
        ScheduleFrequency frequency,
        int weekday,
        int day,
        TimeOnly time
    ) =>
        frequency switch
        {
            ScheduleFrequency.Hour => ScheduleCron.Hourly,
            ScheduleFrequency.Weekday => $"{time.Minute} {time.Hour} * * {ScheduleCron.Weekdays}",
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
        ParseKind(trigger.Kind) switch
        {
            TriggerKind.Launchpad => ChainName(trigger.ChainId)
                + (
                    !string.IsNullOrEmpty(trigger.SourceTable)
                        ? $" · {trigger.SourceTable}"
                        : string.Empty
                ),
            _ => JobName(trigger.JobId ?? Guid.Empty),
        };

    public async Task AddTriggerAsync(Guid projectId)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            Error = ScheduleCopy.NameRequired;
            NotifyStateChanged();
            return;
        }

        Busy = true;
        try
        {
            if (NewKind == TriggerKind.Launchpad)
            {
                if (NewChainId == Guid.Empty)
                {
                    Error = ScheduleCopy.ChainRequired;
                    return;
                }

                if (string.IsNullOrWhiteSpace(NewPrompt))
                {
                    Error = ScheduleCopy.PromptRequired;
                    return;
                }

                await _service.CreateTriggerAsync(
                    new CreateTriggerCommand(
                        null,
                        NewName.Trim(),
                        NewKind.ToString(),
                        SelectedCron(),
                        null,
                        NewChainId,
                        string.IsNullOrWhiteSpace(NewSourceTable) ? null : NewSourceTable,
                        NewPrompt.Trim()
                    )
                );
            }
            else
            {
                if (NewJobId == Guid.Empty)
                {
                    Error = ScheduleCopy.JobRequired;
                    return;
                }

                await _service.CreateTriggerAsync(
                    new CreateTriggerCommand(
                        NewJobId,
                        NewName.Trim(),
                        NewKind.ToString(),
                        NewKind == TriggerKind.Schedule ? SelectedCron() : null,
                        NewKind == TriggerKind.Event ? NewEvent : null
                    )
                );
            }

            Triggers = await _service.ListTriggersAsync(projectId);
            NewName = string.Empty;
            NewPrompt = string.Empty;
            NewSourceTable = string.Empty;
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
            Triggers = await _service.ListTriggersAsync(projectId);
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
            Triggers = await _service.ListTriggersAsync(projectId);
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
            EditError = ScheduleCopy.NameRequired;
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
            Triggers = await _service.ListTriggersAsync(projectId);
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
        Jobs?.FirstOrDefault(job => job.Id == jobId)?.Name ?? ScheduleCopy.DeletedJob;

    private string ChainName(Guid? chainId) =>
        Chains?.FirstOrDefault(chain => chain.Id == chainId)?.Name ?? ScheduleCopy.DeletedChain;

    private static TriggerKind ParseKind(string kind) =>
        Enum.TryParse<TriggerKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
}
