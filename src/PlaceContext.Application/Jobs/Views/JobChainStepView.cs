namespace PlaceContext.Application.Dtos;

/// <summary>One job within a chain stage. JobName is "(deleted)" when the job no longer exists.</summary>
public sealed record JobChainStepView(Guid JobId, string JobName);
