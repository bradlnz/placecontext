using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class CrmAutomationRuleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Stage_specific_rule_only_matches_its_event_and_stage()
    {
        var rule = CrmAutomationRule.Create(
            Guid.NewGuid(), "Start onboarding", CrmAutomationEventType.StageEntered,
            CustomerLifecycleStage.Onboarding, Guid.NewGuid(), true, T0);

        Assert.True(rule.Matches(
            CrmAutomationEventType.StageEntered, CustomerLifecycleStage.Onboarding));
        Assert.False(rule.Matches(
            CrmAutomationEventType.StageEntered, CustomerLifecycleStage.Active));
        Assert.False(rule.Matches(
            CrmAutomationEventType.ClientUpdated, CustomerLifecycleStage.Onboarding));
    }

    [Fact]
    public void Rule_without_stage_filter_matches_any_lifecycle_stage()
    {
        var rule = CrmAutomationRule.Create(
            Guid.NewGuid(), "Process new note", CrmAutomationEventType.NoteAdded,
            null, Guid.NewGuid(), true, T0);

        Assert.All(Enum.GetValues<CustomerLifecycleStage>(),
            stage => Assert.True(rule.Matches(CrmAutomationEventType.NoteAdded, stage)));
    }

    [Fact]
    public void Disabled_rule_does_not_match()
    {
        var rule = CrmAutomationRule.Create(
            Guid.NewGuid(), "Paused rule", CrmAutomationEventType.ClientCreated,
            null, Guid.NewGuid(), false, T0);

        Assert.False(rule.Matches(
            CrmAutomationEventType.ClientCreated, CustomerLifecycleStage.Lead));
    }
}
