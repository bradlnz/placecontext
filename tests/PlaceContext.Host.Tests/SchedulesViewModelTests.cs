using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class SchedulesViewModelTests
{
    [Theory]
    [InlineData(ScheduleFrequency.Hour, 9, 0, 1, 1, "0 * * * *")]
    [InlineData(ScheduleFrequency.Day, 9, 15, 1, 1, "15 9 * * *")]
    [InlineData(ScheduleFrequency.Weekday, 9, 15, 1, 1, "15 9 * * 1-5")]
    [InlineData(ScheduleFrequency.Week, 9, 15, 3, 1, "15 9 * * 3")]
    [InlineData(ScheduleFrequency.Month, 9, 15, 1, 31, "15 9 28 * *")]
    public void ComposeCron_translates_typed_schedule_fields(
        ScheduleFrequency frequency,
        int hour,
        int minute,
        int weekday,
        int day,
        string expected
    )
    {
        var actual = SchedulesViewModel.ComposeCron(
            frequency,
            weekday,
            day,
            new TimeOnly(hour, minute)
        );

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(TriggerKind.Schedule)]
    [InlineData(TriggerKind.Event)]
    public void Editable_trigger_kinds_are_typed(TriggerKind kind)
    {
        Assert.Contains(kind, SchedulesViewModel.EditableTriggerKinds);
    }
}
