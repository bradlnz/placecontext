using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Controllers;

public sealed record CustomerPortalJobChainStepView(
    int Index,
    Guid JobId,
    string JobName,
    IReadOnlyList<JobParameterDto> Parameters,
    string? ConditionExpression);
