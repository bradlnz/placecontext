using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Application.ProjectData;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ProjectDataDatabaseTests
{
    [Fact]
    public void Legacy_database_route_keeps_the_owner_or_super_admin_policy()
    {
        var attributes = typeof(PlaceContext.Host.Components.Pages.DatabaseSettings)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, attribute => attribute.Policy == Policies.DatabaseInspection);
    }

    [Fact]
    public async Task Sql_studio_lists_application_tables_and_routes_queries_to_the_protected_handler()
    {
        var dispatcher = new Queries();
        var vm = new ProjectDataViewModel(new PlaceContextService(dispatcher, null!, null!, null!), new PortalUiState(), new NoJs(), dispatcher);
        vm.Initialize(Guid.NewGuid(), applicationDatabase: true);
        await vm.LoadAsync();
        Assert.True(vm.ApplicationDatabase);
        Assert.True(vm.TablesReady);
        Assert.All(vm.Tables, table => Assert.True(table.ReadOnly));
        Assert.Equal(2, vm.ApplicationTables.Count);
        Assert.Equal("SELECT * FROM \"public\".\"users\" LIMIT 50;", await vm.TablePreviewSqlAsync("public.users"));
        var result = await vm.QueryApplicationDatabaseAsync("SELECT COUNT(*) FROM users");
        Assert.Equal("2", Assert.Single(Assert.Single(result.Rows)));
        Assert.Equal("SELECT COUNT(*) FROM users", dispatcher.Sql);
    }

    [Fact]
    public async Task Revocation_clears_the_application_catalog_and_does_not_leave_stale_tables()
    {
        var dispatcher = new Queries();
        var vm = new ProjectDataViewModel(new PlaceContextService(dispatcher, null!, null!, null!), new PortalUiState(), new NoJs(), dispatcher);
        vm.Initialize(Guid.NewGuid(), applicationDatabase: true);
        await vm.LoadAsync();
        dispatcher.Deny = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => vm.QueryApplicationDatabaseAsync("SELECT * FROM users"));
        Assert.Empty(vm.Tables);
        Assert.Empty(vm.ApplicationTables);
        Assert.NotNull(vm.Error);
        dispatcher.Deny = false;
        await vm.LoadAsync();
        Assert.Null(vm.Error);
        Assert.NotEmpty(vm.Tables);
    }

    private sealed class Queries : IDispatcher
    {
        public bool Deny { get; set; }
        public string? Sql { get; private set; }
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            if (Deny)
            {
                throw new UnauthorizedAccessException();
            }
            object result = query switch
            {
                ListDatabaseTablesQuery => new DatabaseTable[] { new("public.users", [], true), new("public.global", [], false) },
                QueryDatabaseQuery request => QueryResult(request.Sql),
                _ => throw new InvalidOperationException("Application tables must not query project data.")
            };
            return Task.FromResult((TResult)result);
        }
        private ProjectQueryResult QueryResult(string sql)
        {
            Sql = sql;
            return new(["count"], [new string?[] { "2" }], 0, false);
        }
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
