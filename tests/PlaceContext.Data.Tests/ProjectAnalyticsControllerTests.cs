using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Analytics;
using PlaceContext.Data.Contracts.Api;
using PlaceContext.Data.Controllers;

namespace PlaceContext.Data.Tests;

public sealed class ProjectAnalyticsControllerTests
{
    [Fact]
    public void Controller_preserves_the_analytics_route_and_data_read_policy()
    {
        var type = typeof(ProjectAnalyticsController);

        Assert.Equal("api/v1/projects/{projectId:guid}/analytics", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(Permission.DataRead, type.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Fact]
    public void Data_api_registers_sql_chart_handlers_for_the_runtime_controller()
    {
        var services = new ServiceCollection();

        services.AddDataApi();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<SaveSqlChartCommand, ProjectChartView>)
            && descriptor.ImplementationType == typeof(SaveSqlChartHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<DeleteSqlChartCommand, bool>)
            && descriptor.ImplementationType == typeof(DeleteSqlChartHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AnalyticsRefreshQueue));
    }

    [Fact]
    public async Task Get_dispatches_data_queries_and_maps_pending_tables()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            QueryResult = query => query switch
            {
                ListProjectDataTablesQuery => new List<ProjectTableInfo>
                {
                    new("population", 42),
                    new("permits", 7),
                },
                ListProjectChartsQuery => new List<ProjectChartView>(),
                _ => throw new InvalidOperationException("Unexpected query."),
            },
        };
        var tenant = new StubTenant("UTC");
        var queue = new AnalyticsRefreshQueue(new StubOperationNotifier());
        queue.TryEnqueue(
            new TenantContext(tenant.TenantId, tenant.Slug, tenant.TimeZoneId),
            projectId,
            tableName: "population");
        var controller = new ProjectAnalyticsController(dispatcher, tenant, queue);

        var result = await controller.Get(projectId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AnalyticsPageResponse>(ok.Value);
        Assert.Equal(2, response.Tables.Count);
        Assert.True(response.SweepPending);
        Assert.Equal(["population"], response.PendingTables);
    }

    [Fact]
    public void Queue_refresh_requires_a_resolved_tenant()
    {
        var controller = CreateController(new StubDispatcher(), new StubTenant("UTC", isResolved: false));

        var result = controller.QueueRefresh(
            Guid.NewGuid(),
            new QueueAnalyticsRefreshRequest(null, null));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public void Queue_refresh_preserves_duplicate_pending_behavior()
    {
        var projectId = Guid.NewGuid();
        var tenant = new StubTenant("UTC");
        var controller = CreateController(new StubDispatcher(), tenant);
        var request = new QueueAnalyticsRefreshRequest("population", "group by locality");

        var first = controller.QueueRefresh(projectId, request);
        var duplicate = controller.QueueRefresh(projectId, request);

        var firstResponse = Assert.IsType<AnalyticsMessageResponse>(
            Assert.IsType<AcceptedResult>(first.Result).Value);
        var duplicateResponse = Assert.IsType<AnalyticsMessageResponse>(
            Assert.IsType<AcceptedResult>(duplicate.Result).Value);
        Assert.Equal("Chart generation queued.", firstResponse.Message);
        Assert.Equal("That chart generation is already pending.", duplicateResponse.Message);
    }

    [Fact]
    public async Task Save_sql_chart_dispatches_to_data_and_maps_the_chart_contract()
    {
        var projectId = Guid.NewGuid();
        var generatedAt = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
        var dispatcher = new StubDispatcher
        {
            CommandResult = _ => new ProjectChartView(
                "sql:Population",
                "{\"labels\":[\"North\"],\"series\":[],\"sql\":\"select 1\",\"type\":\"line\"}",
                generatedAt),
        };
        var controller = CreateController(dispatcher, new StubTenant("Europe/London"));

        var result = await controller.SaveSqlChart(
            projectId,
            new SaveSqlChartRequest(" Population ", "select 1", "line"),
            default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AnalyticsChartResponse>(ok.Value);
        var command = Assert.IsType<SaveSqlChartCommand>(dispatcher.LastCommand);
        Assert.Equal(projectId, command.ProjectId);
        Assert.Equal("Population", command.Name);
        Assert.Equal("select 1", command.Sql);
        Assert.Equal("line", command.ChartType);
        Assert.Equal("Population", response.Name);
        Assert.Equal("sql:Population", response.TableName);
        Assert.Equal("2026-07-09 11:00", response.GeneratedAtDisplay);
        Assert.Equal("select 1", response.Sql);
        Assert.Equal("line", response.ChartType);
        Assert.Null(response.LegacyHtml);
    }

    [Theory]
    [InlineData("", "select 1", "bar", "Chart name is required.")]
    [InlineData("name", "", "bar", "SQL query is required.")]
    [InlineData("name", "select 1", "scatter", "Chart type must be bar, line, or pie.")]
    public async Task Save_sql_chart_rejects_invalid_requests_before_dispatch(
        string name,
        string sql,
        string chartType,
        string expectedError)
    {
        var dispatcher = new StubDispatcher();
        var controller = CreateController(dispatcher, new StubTenant("UTC"));

        var result = await controller.SaveSqlChart(
            Guid.NewGuid(), new SaveSqlChartRequest(name, sql, chartType), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(expectedError, badRequest.Value!.ToString());
        Assert.Null(dispatcher.LastCommand);
    }

    [Fact]
    public async Task Save_sql_chart_returns_handler_validation_as_bad_request()
    {
        var dispatcher = new StubDispatcher
        {
            CommandException = new InvalidOperationException("The query result isn't chartable."),
        };
        var controller = CreateController(dispatcher, new StubTenant("UTC"));

        var result = await controller.SaveSqlChart(
            Guid.NewGuid(), new SaveSqlChartRequest("name", "select label", "bar"), default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("The query result isn't chartable.", badRequest.Value!.ToString());
    }

    [Fact]
    public async Task Delete_sql_chart_dispatches_to_data_and_preserves_not_found_behavior()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new StubDispatcher { CommandResult = _ => false };
        var controller = CreateController(dispatcher, new StubTenant("UTC"));

        var result = await controller.DeleteSqlChart(projectId, "Population", default);

        Assert.IsType<NotFoundObjectResult>(result);
        var command = Assert.IsType<DeleteSqlChartCommand>(dispatcher.LastCommand);
        Assert.Equal(projectId, command.ProjectId);
        Assert.Equal("Population", command.Name);
    }

    private static ProjectAnalyticsController CreateController(
        IDispatcher dispatcher,
        ICurrentTenant tenant) =>
        new(dispatcher, tenant, new AnalyticsRefreshQueue(new StubOperationNotifier()));

    private sealed class StubDispatcher : IDispatcher
    {
        public Func<object, object?>? CommandResult { get; init; }
        public Func<object, object?>? QueryResult { get; init; }
        public Exception? CommandException { get; init; }
        public object? LastCommand { get; private set; }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            LastCommand = command;
            if (CommandException is not null) throw CommandException;
            return Task.FromResult((TResult)CommandResult!(command)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default) =>
            Task.FromResult((TResult)QueryResult!(query)!);
    }

    private sealed class StubTenant(string timeZoneId, bool isResolved = true) : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => timeZoneId;
        public bool IsResolved => isResolved;
    }

    private sealed class StubOperationNotifier : IBackgroundOperationNotifier
    {
        public Guid Track(TenantContext tenant, Guid? projectId, string title, string? link, string? correlationKey = null) =>
            Guid.NewGuid();
        public void MarkRunning(Guid operationId) { }
        public void MarkDone(Guid operationId, string? outcome = null) { }
        public void MarkFailed(Guid operationId, string error) { }
    }
}
