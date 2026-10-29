using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record DeleteJobTestCaseCommand(Guid TestId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
