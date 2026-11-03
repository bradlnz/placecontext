using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class DatabaseSettingsViewModelTests
{
    [Fact]
    public void Database_page_uses_the_owner_or_super_admin_policy()
    {
        var attributes = typeof(PlaceContext.Host.Components.Pages.DatabaseSettings)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, attribute => attribute.Policy == Policies.DatabaseInspection);
    }

    [Fact]
    public async Task Browser_pages_records_and_never_requests_global_rows()
    {
        var dispatcher = new Queries();
        var vm = new DatabaseSettingsViewModel(dispatcher, new PortalUiState());
        await vm.LoadAsync();
        vm.Filter = "USERS";
        Assert.Single(vm.FilteredTables);
        await vm.SelectAsync(Queries.Users);
        Assert.True(vm.CanNext);
        Assert.False(vm.CanPrevious);
        await vm.NextAsync();
        Assert.Equal(50, vm.Page!.Offset);
        Assert.True(vm.CanPrevious);
        await vm.PreviousAsync();
        Assert.Equal(0, vm.Page!.Offset);
        var reads = dispatcher.Reads;
        await vm.SelectAsync(Queries.Global);
        Assert.Null(vm.Page);
        Assert.False(vm.CanNext);
        Assert.Equal(reads, dispatcher.Reads);
    }

    [Fact]
    public async Task Revocation_clears_previous_rows_and_error_does_not_leave_browser_busy()
    {
        var dispatcher = new Queries();
        var vm = new DatabaseSettingsViewModel(dispatcher, new PortalUiState());
        await vm.LoadAsync();
        await vm.SelectAsync(Queries.Users);
        dispatcher.Deny = true;
        await vm.NextAsync();
        Assert.Null(vm.Page);
        Assert.Null(vm.Selected);
        Assert.Empty(vm.Tables);
        Assert.NotNull(vm.Error);
        Assert.False(vm.Busy);
        dispatcher.Deny = false;
        await vm.LoadAsync();
        Assert.Null(vm.Error);
        Assert.NotEmpty(vm.Tables);
    }

    private sealed class Queries : IDispatcher
    {
        public static readonly DatabaseTable Users = new("public.users", [], true);
        public static readonly DatabaseTable Global = new("public.global", [], false);
        public bool Deny { get; set; }
        public int Reads { get; private set; }
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            if (Deny)
            {
                throw new UnauthorizedAccessException();
            }
            if (query is ListDatabaseTablesQuery)
            {
                return Task.FromResult((TResult)(object)new[] { Users, Global });
            }
            var read = Assert.IsType<ReadDatabaseTableQuery>(query);
            Reads++;
            return Task.FromResult((TResult)(object)new DatabaseTablePage(Users, [], read.Offset, read.Offset == 0));
        }
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
