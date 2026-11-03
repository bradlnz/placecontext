using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record DeleteJobTestCaseCommand(Guid TestId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
