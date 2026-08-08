using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record UpdateJobTestCodeCommand(
    Guid TestId,
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<CodeFileDto> CodeFiles,
    bool AllowNetworkEgress) : ICommand<JobTestCaseView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
