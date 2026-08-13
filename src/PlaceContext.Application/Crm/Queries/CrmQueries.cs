using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListCrmClientsQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmClientView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record ListCrmClientChainRunsQuery(Guid ClientId, int Take = 20)
    : IQuery<IReadOnlyList<CrmChainRunView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record ListCrmUsersQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmUserView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.MembersManage;
}

public sealed record ListCrmClientAssignedJobChainsQuery(
    Guid ClientId,
    Guid ProjectId)
    : IQuery<IReadOnlyList<Guid>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmAutomationManage;
}

public sealed record ListCrmClientAssignedUsersQuery(
    Guid ClientId,
    Guid ProjectId)
    : IQuery<IReadOnlyList<Guid>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.MembersManage;
}

public sealed record ListCrmClientCommunicationsQuery(Guid ClientId, int Take = 100)
    : IQuery<IReadOnlyList<CrmCommunicationView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record GetCrmCommsCapabilitiesQuery : IQuery<CrmCommsCapabilitiesView>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record ListCrmClientArtifactsQuery(Guid ClientId, int Take = 200)
    : IQuery<IReadOnlyList<CrmClientArtifactView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record ListCrmAppointmentsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmAppointmentView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

public sealed record ListCrmCalendarsQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmCalendarView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}
