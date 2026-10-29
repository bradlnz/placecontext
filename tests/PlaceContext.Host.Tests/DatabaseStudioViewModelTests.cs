using Microsoft.JSInterop;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class DatabaseStudioViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_reads_edits_from_both_monaco_and_its_fallback(bool richEditor)
    {
        var databases = new Databases();
        var js = new Js { RichEditor = richEditor };
        var vm = new DatabaseStudioViewModel(databases, js);
        await vm.LoadAsync(Guid.NewGuid(), databases.Id);
        await vm.AfterRenderAsync();
        js.Value = "SELECT 42 AS answer";
        await vm.RunAsync();
        Assert.Equal("SELECT 42 AS answer", databases.LastQuery);
        Assert.Null(vm.Error);
        await vm.AfterRenderAsync();
        Assert.Equal(1, js.Initializations);
        await vm.DisposeAsync();
        Assert.False(js.Mounted);
    }

    [Fact]
    public async Task Database_navigation_recreates_an_editor_removed_by_the_loading_view()
    {
        var databases = new Databases();
        var js = new Js();
        var vm = new DatabaseStudioViewModel(databases, js);
        var project = Guid.NewGuid();
        await vm.LoadAsync(project, databases.Id);
        await vm.AfterRenderAsync();
        databases.Id = Guid.NewGuid();
        await vm.LoadAsync(project, databases.Id);
        js.Mounted = false;
        await vm.AfterRenderAsync();
        Assert.Equal(2, js.Initializations);
        js.Value = "SELECT 'second database'";
        await vm.RunAsync();
        Assert.Equal(js.Value, databases.LastQuery);
        Assert.Equal(databases.Id, databases.LastId);
    }

    [Fact]
    public async Task Explicit_queries_are_not_overwritten_by_a_stale_editor()
    {
        var databases = new Databases();
        var js = new Js();
        var vm = new DatabaseStudioViewModel(databases, js);
        await vm.LoadAsync(Guid.NewGuid(), databases.Id);
        await vm.AfterRenderAsync();
        js.IgnoreSetValue = true;
        await vm.SelectAsync("SELECT * FROM \"public\".\"items\" LIMIT 100");
        Assert.Equal("SELECT * FROM \"public\".\"items\" LIMIT 100", databases.LastQuery);
        await vm.SelectAsync("SELECT 'plain text' AS source");
        Assert.Equal("SELECT 'plain text' AS source", databases.LastQuery);
    }

    [Fact]
    public async Task A_missing_editor_does_not_replace_the_query_with_null()
    {
        var databases = new Databases();
        var js = new Js();
        var vm = new DatabaseStudioViewModel(databases, js);
        await vm.LoadAsync(Guid.NewGuid(), databases.Id);
        await vm.AfterRenderAsync();
        vm.Query = "SELECT 7";
        js.Mounted = false;
        await vm.RunAsync();
        Assert.Equal("SELECT 7", databases.LastQuery);
        Assert.Null(vm.Error);
    }

    private sealed class Js : IJSRuntime
    {
        public bool RichEditor = true;
        public bool Mounted;
        public bool IgnoreSetValue;
        public string? Value;
        public int Initializations;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args)
        {
            object? result = null;
            switch (name)
            {
                case "pcmonaco.init": Initializations++; Mounted = true; Value = (string)args![1]!; result = RichEditor; break;
                case "pcmonaco.isMounted": result = Mounted; break;
                case "pcmonaco.getValue": result = Mounted ? Value : null; break;
                case "pcmonaco.setValue": if (!IgnoreSetValue) Value = (string)args![1]!; break;
                case "pcmonaco.destroy": Mounted = false; break;
                default: throw new InvalidOperationException(name);
            }
            return ValueTask.FromResult(result is null ? default! : (T)result);
        }
    }

    private sealed class Databases : IDatabaseConsole
    {
        public Guid Id = Guid.NewGuid();
        public Guid LastId;
        public string? LastQuery;
        public Task<IReadOnlyList<DatabaseInstance>> ListAsync(Guid projectId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DatabaseInstance>>([new(Id, "Test", "postgres", "running", "ready", "localhost", 5432, "app", "app", "data", DateTimeOffset.UtcNow)]);
        public Task<DatabaseQueryResult> QueryAsync(Guid projectId, Guid databaseId, string query, string? cursor = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
            LastId = databaseId;
            LastQuery = query;
            return Task.FromResult(new DatabaseQueryResult(["table_schema", "table_name"], [["public", "items"]]));
        }
        public Task CreateAsync(Guid projectId, DatabaseLaunch database, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ControlAsync(Guid projectId, Guid databaseId, string action, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> LogsAsync(Guid projectId, Guid databaseId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
