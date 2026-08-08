namespace PlaceContext.Jobs.Contracts.Management;

/// <summary>Updates the supplied fields on an existing trigger.</summary>
public sealed record UpdateScheduleRequest(
    string? Name = null,
    string? CronExpression = null,
    string? EventName = null,
    bool? Enabled = null);
