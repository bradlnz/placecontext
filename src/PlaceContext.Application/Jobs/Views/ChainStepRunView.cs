namespace PlaceContext.Application.Dtos;

/// <summary>One executed chain step and the job run it produced.</summary>
public sealed record ChainStepRunView(int Index, Guid JobId, string JobName, Guid RunId, string Status);
