namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SchedulePageResponse(
    string TimeZoneId,
    IReadOnlyList<ScheduleTargetResponse> Jobs,
    IReadOnlyList<ScheduleTargetResponse> Chains,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<ScheduleTriggerResponse> Triggers);
