using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
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
        var controller = new ProjectAnalyticsController(dispatcher, new StubTenant("Europe/London"));

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
        var controller = new ProjectAnalyticsController(dispatcher, new StubTenant("UTC"));

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
        var controller = new ProjectAnalyticsController(dispatcher, new StubTenant("UTC"));

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
        var controller = new ProjectAnalyticsController(dispatcher, new StubTenant("UTC"));

        var result = await controller.DeleteSqlChart(projectId, "Population", default);

        Assert.IsType<NotFoundObjectResult>(result);
        var command = Assert.IsType<DeleteSqlChartCommand>(dispatcher.LastCommand);
        Assert.Equal(projectId, command.ProjectId);
        Assert.Equal("Population", command.Name);
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public Func<object, object?>? CommandResult { get; init; }
        public Exception? CommandException { get; init; }
        public object? LastCommand { get; private set; }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            LastCommand = command;
            if (CommandException is not null) throw CommandException;
            return Task.FromResult((TResult)CommandResult!(command)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubTenant(string timeZoneId) : ICurrentTenant
    {
        public Guid TenantId => Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => timeZoneId;
        public bool IsResolved => true;
    }
}
