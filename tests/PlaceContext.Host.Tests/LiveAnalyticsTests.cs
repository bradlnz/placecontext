using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class LiveAnalyticsTests
{
    [Fact]
    public async Task Saving_uses_current_monaco_text_and_refreshing_never_sends_a_write_command()
    {
        var dispatcher = new Dispatcher();
        using var vm = new ProjectAnalyticsViewModel(new PlaceContextService(dispatcher, null!, null!, null!),
            new PortalUiState(), new Editor(), new Queue());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.EditSql(new("sql:Orders", "{\"sql\":\"SELECT old\",\"type\":\"line\"}", DateTimeOffset.UtcNow));
        await vm.RenderAsync();
        vm.SqlName = "Orders";
        await vm.SaveSqlAsync();
        var save = Assert.IsType<SaveSqlChartCommand>(Assert.Single(dispatcher.Commands));
        Assert.Equal("SELECT status, count(*) FROM orders GROUP BY status", save.Sql);
        Assert.Equal("line", save.ChartType);
        dispatcher.Commands.Clear();
        await vm.ReloadAsync();
        Assert.Empty(dispatcher.Commands);
        Assert.All(dispatcher.Queries.OfType<ListProjectChartsQuery>(), q => Assert.True(q.Live));
    }

    private sealed class Dispatcher : IDispatcher
    {
        public List<object> Queries { get; } = [];
        public List<object> Commands { get; } = [];
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            Queries.Add(query);
            object result = query switch
            {
                ListProjectChartsQuery => Array.Empty<ProjectChartView>(),
                ListProjectDataTablesQuery => Array.Empty<ProjectTableInfo>(),
                ListProjectTableSchemasQuery => Array.Empty<ProjectTableSchema>(),
                _ => throw new InvalidOperationException()
            };
            return Task.FromResult((T)result);
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            Commands.Add(command);
            return Task.FromResult((T)(object)new ProjectChartView("sql:Orders", "{}", DateTimeOffset.UtcNow));
        }
    }

    private sealed class Editor : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
        {
            object? result = identifier switch
            {
                "pcmonaco.init" => true,
                "pcmonaco.getValue" => "SELECT status, count(*) FROM orders GROUP BY status",
                _ => default(T)
            };
            return ValueTask.FromResult((T)result!);
        }
    }

    private sealed class Queue : IAnalyticsRefreshQueue
    {
        public bool IsPending(Guid projectId) => false;
        public bool IsPending(Guid projectId, string? tableName) => false;
        public bool TryEnqueue(PlaceContext.Application.Tenancy.TenantInfo tenant, Guid projectId, string projectName = "project", string? tableName = null, string? instruction = null) => false;
    }
}
