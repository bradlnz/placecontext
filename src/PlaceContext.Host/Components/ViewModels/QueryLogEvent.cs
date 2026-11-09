using System.Globalization;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record QueryLogEvent(RunReportView? Report, JobLogEvent Event, ServiceLogSource? Service = null)
{
    public DateTimeOffset? Timestamp => DateTimeOffset.TryParse(Event.Timestamp, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    public string Field(string name) => name switch
    {
        "job" => Report?.JobName ?? Service?.Name ?? "",
        "project" => Report?.ProjectName ?? Service?.ProjectName ?? "",
        "run" => Report?.Run.Id.ToString() ?? "",
        "source" => Service is null ? "job" : "service",
        "service" => Service?.Name ?? "",
        "status" => Report?.Run.Status ?? Service?.Status ?? "",
        "level" => Event.Level,
        "stream" => Event.Stream,
        "message" => Event.Message,
        "timestamp" => Event.Timestamp ?? "",
        _ => "",
    };
}
