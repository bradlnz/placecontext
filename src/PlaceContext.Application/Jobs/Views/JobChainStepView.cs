namespace PlaceContext.Application.Dtos;

/// <summary>One step of a chain: the job it runs. JobName is "(deleted)" when the job no longer exists.</summary>
public sealed record JobChainStepView(Guid JobId, string JobName);
