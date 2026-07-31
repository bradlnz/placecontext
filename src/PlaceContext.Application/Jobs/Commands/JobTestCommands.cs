using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

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

public sealed record DeleteJobTestCaseCommand(Guid TestId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}

public sealed record RunJobTestCaseCommand(Guid TestId)
    : ICommand<JobTestCaseView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsRun;
}

public sealed record UpdateJobTestCodeCommand(
    Guid TestId,
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<CodeFileDto> CodeFiles,
    bool AllowNetworkEgress) : ICommand<JobTestCaseView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsEdit;
}
