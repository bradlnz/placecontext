using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record RunCrmClientAutomationCommand(
    Guid ClientId,
    Guid ChainId,
    string? InputPayload = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null)
    : ICommand<CrmChainRunView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsRun;
}
