namespace PlaceContext.Application.Jobs;

public sealed record AppQueueResult(JobChainSubmission Receipt, string? Output);
