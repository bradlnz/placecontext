using PlaceContext.Application.Access;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Cqrs;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Application.Tests;

public sealed class DatabaseInspectionTests
{
    [Fact]
    public async Task Registered_dispatcher_resolves_database_queries_and_enforces_access()
    {
        var identity = new Identity(true, true);
        var administrators = new Administrators(true);
        var inspector = new Inspector();
        var services = new ServiceCollection().AddApplication();
        services.AddSingleton<ICurrentTenant>(identity);
        services.AddSingleton<ICurrentUser>(identity);
        services.AddSingleton<IWorkspaceAdministratorReader>(administrators);
        services.AddSingleton<IDatabaseInspector>(inspector);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Query(new QueryDatabaseQuery("SELECT * FROM users"));
        Assert.Equal(1, inspector.Calls);
        administrators.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            dispatcher.Query(new QueryDatabaseQuery("SELECT * FROM users")));
        Assert.Equal(1, inspector.Calls);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Unauthorized_queries_never_reach_the_database(bool authenticated, bool resolved, bool allowed)
    {
        var identity = new Identity(authenticated, resolved);
        var access = new DatabaseInspectionAccess(identity, identity, new Administrators(allowed));
        var inspector = new Inspector();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ListDatabaseTablesHandler(access, inspector).HandleAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ReadDatabaseTableHandler(access, inspector).HandleAsync(new("public.users")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new QueryDatabaseHandler(access, inspector).HandleAsync(new("SELECT * FROM users")));
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task Access_is_rechecked_after_role_revocation_and_invalid_pages_are_rejected()
    {
        var identity = new Identity(true, true);
        var administrators = new Administrators(true);
        var access = new DatabaseInspectionAccess(identity, identity, administrators);
        var inspector = new Inspector();
        var list = new ListDatabaseTablesHandler(access, inspector);
        var read = new ReadDatabaseTableHandler(access, inspector);
        await list.HandleAsync(new());
        await read.HandleAsync(new("public.users"));
        Assert.Equal(2, inspector.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => read.HandleAsync(new("public.users", -1)));
        await Assert.ThrowsAsync<ArgumentException>(() => read.HandleAsync(new(" ")));
        Assert.Equal(2, inspector.Calls);
        administrators.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read.HandleAsync(new("public.users", 50)));
        Assert.Equal(2, inspector.Calls);
        Assert.Equal(identity.TenantId, administrators.TenantId);
        Assert.Equal(identity.UserId, administrators.UserId);
    }

    private sealed class Identity(bool authenticated, bool resolved) : ICurrentTenant, ICurrentUser
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => resolved;
        public bool IsAuthenticated => authenticated;
        public string Role => "Owner";
    }

    private sealed class Administrators(bool allowed) : IWorkspaceAdministratorReader
    {
        public bool Allowed { get; set; } = allowed;
        public Guid TenantId { get; private set; }
        public Guid UserId { get; private set; }
        public Task<bool> IsOwnerOrAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            TenantId = tenantId;
            UserId = userId;
            return Task.FromResult(Allowed);
        }
        public Task<bool> IsAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class Inspector : IDatabaseInspector
    {
        public int Calls { get; private set; }
        public Task<PlaceContext.Application.ProjectData.ProjectQueryResult> QueryAsync(DatabaseSelectStatement query, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new PlaceContext.Application.ProjectData.ProjectQueryResult([], [], 0, false));
        }
        public Task<IReadOnlyList<DatabaseTable>> ListTablesAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<DatabaseTable>>([]);
        }
        public Task<DatabaseTablePage> ReadTableAsync(string table, int offset, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new DatabaseTablePage(new(table, [], true), [], offset, false));
        }
    }
}
