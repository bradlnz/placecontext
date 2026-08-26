using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class DesktopApiAuthorizationTests
{
    [Fact]
    public void Controller_requires_desktop_oauth_bearer_policy()
    {
        var authorize = Assert.Single(typeof(DesktopApiController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("DesktopApi", authorize.Policy);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authorize.AuthenticationSchemes);
    }

    [Theory]
    [InlineData(nameof(DesktopApiController.ListProjects), Permission.ProjectsView)]
    [InlineData(nameof(DesktopApiController.ListJobs), Permission.JobsView)]
    [InlineData(nameof(DesktopApiController.ListRuns), Permission.JobsView)]
    [InlineData(nameof(DesktopApiController.RunJob), Permission.JobsRun)]
    [InlineData(nameof(DesktopApiController.ListTests), Permission.JobsView)]
    [InlineData(nameof(DesktopApiController.RunTest), Permission.JobsRun)]
    [InlineData(nameof(DesktopApiController.ListChains), Permission.JobsView)]
    [InlineData(nameof(DesktopApiController.RunChain), Permission.JobsRun)]
    [InlineData(nameof(DesktopApiController.ListSchedules), Permission.JobsView)]
    [InlineData(nameof(DesktopApiController.SetScheduleEnabled), Permission.TriggersManage)]
    [InlineData(nameof(DesktopApiController.ListDataResources), Permission.DataRead)]
    [InlineData(nameof(DesktopApiController.QueryData), Permission.DataRead)]
    [InlineData(nameof(DesktopApiController.ListSecrets), Permission.SecretsManage)]
    [InlineData(nameof(DesktopApiController.ListAgents), Permission.AgentsChat)]
    [InlineData(nameof(DesktopApiController.ListAgentChats), Permission.AgentsChat)]
    [InlineData(nameof(DesktopApiController.GetAgentChat), Permission.AgentsChat)]
    [InlineData(nameof(DesktopApiController.SendAgentMessage), Permission.AgentsChat)]
    [InlineData(nameof(DesktopApiController.ListArtifacts), Permission.ArtifactsView)]
    [InlineData(nameof(DesktopApiController.ListObservability), Permission.JobsView)]
    public void Data_actions_enforce_existing_user_permissions(string methodName, string permission)
    {
        var method = typeof(DesktopApiController).GetMethod(methodName);
        Assert.NotNull(method);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(permission, authorize.Policy);
    }
}
