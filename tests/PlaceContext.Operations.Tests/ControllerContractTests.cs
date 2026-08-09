using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Operations.Contracts.Api;
using PlaceContext.Operations.Controllers;

namespace PlaceContext.Operations.Tests;

public sealed class ControllerContractTests
{
    [Fact]
    public void Backup_routes_and_default_admin_policy_are_preserved()
    {
        Assert.Equal("backup", Route<BackupController>());
        Assert.Equal("DefaultAdmin", Authorize<BackupController>().Policy);
        Assert.Equal("export", HttpGet<BackupController>(nameof(BackupController.Export)));
        Assert.Equal("jobs-code", HttpGet<BackupController>(nameof(BackupController.ExportJobsCode)));
        Assert.Equal("import", HttpPost<BackupController>(nameof(BackupController.Import)));
    }

    [Fact]
    public void Inspector_route_cookie_scheme_and_jobs_permission_are_preserved()
    {
        var authorize = Authorize<InspectorController>();

        Assert.Equal("api/v1/inspector", Route<InspectorController>());
        Assert.Equal(Permission.JobsView, authorize.Policy);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authorize.AuthenticationSchemes);
        Assert.Equal("tool-calls", HttpGet<InspectorController>(nameof(InspectorController.GetToolCalls)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(101, 100)]
    public async Task Inspector_clamps_take_and_maps_the_wire_response(int requested, int expected)
    {
        var service = DispatchProxy.Create<IPlaceContextService, InspectorServiceProxy>();
        var proxy = (InspectorServiceProxy)(object)service;
        var at = new DateTimeOffset(2026, 8, 9, 1, 2, 3, TimeSpan.Zero);
        proxy.Calls =
        [
            new ToolCallView(
                "call-1",
                "search",
                "outbound",
                "project",
                "summary",
                "ok",
                42,
                "{\"request\":true}",
                "{\"response\":true}",
                at),
        ];

        var result = await new InspectorController(service).GetToolCalls(requested);

        Assert.Equal(expected, proxy.RequestedTake);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.Single(
            Assert.IsAssignableFrom<IEnumerable<InspectorToolCallResponse>>(ok.Value));
        Assert.Equal(
            new InspectorToolCallResponse(
                "call-1",
                "search",
                "outbound",
                "project",
                "summary",
                "ok",
                42,
                "{\"request\":true}",
                "{\"response\":true}",
                at),
            response);
    }

    [Fact]
    public async Task Backup_import_keeps_invalid_json_as_a_bad_request()
    {
        var service = DispatchProxy.Create<IPlaceContextService, InspectorServiceProxy>();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream("{"u8.ToArray());
        var controller = new BackupController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.Import();

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = badRequest.Value?.GetType().GetProperty("error")?.GetValue(badRequest.Value) as string;
        Assert.StartsWith("Invalid manifest JSON:", error);
    }

    private static string? Route<TController>() =>
        typeof(TController).GetCustomAttribute<RouteAttribute>()?.Template;

    private static AuthorizeAttribute Authorize<TController>() =>
        Assert.IsType<AuthorizeAttribute>(
            Assert.Single(typeof(TController).GetCustomAttributes<AuthorizeAttribute>()));

    private static string? HttpGet<TController>(string method) =>
        typeof(TController).GetMethod(method)?.GetCustomAttribute<HttpGetAttribute>()?.Template;

    private static string? HttpPost<TController>(string method) =>
        typeof(TController).GetMethod(method)?.GetCustomAttribute<HttpPostAttribute>()?.Template;

    private class InspectorServiceProxy : DispatchProxy
    {
        public IReadOnlyList<ToolCallView> Calls { get; set; } = [];
        public int RequestedTake { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IPlaceContextService.GetRecentToolCallsAsync))
            {
                RequestedTake = Assert.IsType<int>(args![0]);
                return Task.FromResult(Calls);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
