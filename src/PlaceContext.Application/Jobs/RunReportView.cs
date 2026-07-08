namespace PlaceContext.Application.Dtos;

/// <summary>One entry in the reporting feed: a run's full detail plus the job it belongs to.</summary>
public sealed record RunReportView(Guid JobId, string JobName, JobRunDetailView Run);
