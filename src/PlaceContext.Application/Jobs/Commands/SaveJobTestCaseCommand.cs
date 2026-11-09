using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record SaveJobTestCaseCommand(
    Guid ProjectId,
    Guid JobId,
    string Name,
    string? InputPayload,
    JobTestAssertionType AssertionType,
    string? ExpectedValue,
    bool Enabled = true,
    Guid? TestId = null) : ICommand<JobTestCaseView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
