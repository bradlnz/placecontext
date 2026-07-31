using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SaveCrmClientCommand(
    Guid ProjectId,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    CustomerLifecycleStage LifecycleStage,
    string? Notes,
    Guid? ClientId = null) : ICommand<CrmClientView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record MoveCrmClientCommand(
    Guid ClientId,
    CustomerLifecycleStage LifecycleStage) : ICommand<CrmClientView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record DeleteCrmClientCommand(Guid ClientId) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record RunCrmClientAutomationCommand(Guid ClientId, Guid ChainId)
    : ICommand<CrmChainRunView>, IRequiresPermission
{
    public string RequiredPermission => Permission.JobsRun;
}

public sealed record AddCrmClientNoteCommand(Guid ClientId, string Body)
    : ICommand<CrmCommunicationView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record SendCrmClientMessageCommand(
    Guid ClientId,
    CrmCommunicationChannel Channel,
    string? Subject,
    string Body) : ICommand<CrmCommunicationView>, IRequiresPermission
{
    public string RequiredPermission => Permission.CrmCommsSend;
}

public sealed record AttachCrmClientArtifactCommand(
    Guid ClientId,
    string FileName,
    string? ContentType,
    byte[] Content) : ICommand<CrmClientArtifactView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record RemoveCrmClientArtifactCommand(Guid ArtifactId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
