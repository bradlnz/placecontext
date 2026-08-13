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

public sealed record SetCrmClientAssignedJobChainsCommand(
    Guid ProjectId,
    Guid ClientId,
    IReadOnlyList<Guid> ChainIds) : ICommand<IReadOnlyList<Guid>>, IRequiresPermission
{
    public string RequiredPermission => Permission.CrmAutomationManage;
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

public sealed record CreateCrmAppointmentCommand(Guid ProjectId, Guid? CalendarId, Guid? ClientId, string Title,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? Notes, Guid? AppointmentId = null)
    : ICommand<CrmAppointmentView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

public sealed record DeleteCrmAppointmentCommand(Guid AppointmentId) : ICommand<bool>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }

public sealed record SaveCrmCalendarCommand(Guid ProjectId, string Name, string Color, Guid? CalendarId = null)
    : ICommand<CrmCalendarView>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }

public sealed record DeleteCrmCalendarCommand(Guid CalendarId) : ICommand<bool>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }

public sealed record CreateCrmUserCommand(
    Guid ProjectId,
    string Email,
    string? Name,
    Guid? ClientId = null)
    : ICommand<CrmUserCreationResult>, IRequiresPermission
{ public string RequiredPermission => Permission.MembersManage; }

public sealed record DeleteCrmUserCommand(Guid ProjectId, Guid CrmUserId)
    : ICommand<bool>, IRequiresPermission
{ public string RequiredPermission => Permission.MembersManage; }

public sealed record CompleteCrmOnboardingCommand(
    string JoinCode,
    string Password,
    string? DisplayName)
    : ICommand<CrmOnboardingResult>;

public sealed record SetCrmClientAssignedUsersCommand(
    Guid ProjectId,
    Guid ClientId,
    IReadOnlyList<Guid> CrmUserIds)
    : ICommand<IReadOnlyList<Guid>>, IRequiresPermission
{ public string RequiredPermission => Permission.DataWrite; }
