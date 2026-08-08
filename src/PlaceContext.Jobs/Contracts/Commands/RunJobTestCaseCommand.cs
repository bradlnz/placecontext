using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record RunJobTestCaseCommand(Guid TestId)
    : ICommand<JobTestCaseView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsRun;
}
