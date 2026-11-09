using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
namespace PlaceContext.Application.Features;

public sealed record DeleteJobTestCaseCommand(Guid TestId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
