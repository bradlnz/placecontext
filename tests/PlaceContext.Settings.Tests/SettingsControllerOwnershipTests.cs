using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers;
using PlaceContext.Settings.Controllers;
using HostSettingsController = PlaceContext.Host.Controllers.Api.SettingsController;

namespace PlaceContext.Settings.Tests;

public sealed class SettingsControllerOwnershipTests
{
    [Fact]
    public void Settings_assembly_owns_the_legacy_controllers()
    {
        var settingsAssembly = typeof(SettingsServiceController).Assembly;

        Assert.Same(settingsAssembly, typeof(HostSettingsController).Assembly);
        Assert.Same(settingsAssembly, typeof(ConnectionsSettingsController).Assembly);
        Assert.Same(settingsAssembly, typeof(BackupSettingsController).Assembly);
    }

    [Fact]
    public void Legacy_route_and_method_contract_is_preserved()
    {
        var actual = new[]
            {
                typeof(HostSettingsController),
                typeof(ConnectionsSettingsController),
                typeof(BackupSettingsController),
            }
            .SelectMany(Routes)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
            {
                "DELETE api/v1/settings/connections/projects/{projectId:guid}/database",
                "DELETE api/v1/settings/connections/projects/{projectId:guid}/index",
                "GET api/v1/settings/artifacts",
                "GET api/v1/settings/branding",
                "GET api/v1/settings/connections/context",
                "GET api/v1/settings/locality",
                "GET api/v1/settings/menu",
                "POST api/v1/settings/artifacts/reset",
                "POST api/v1/settings/backup/imports",
                "POST api/v1/settings/branding/reset",
                "POST api/v1/settings/menu/reset",
                "PUT api/v1/settings/artifacts",
                "PUT api/v1/settings/branding",
                "PUT api/v1/settings/connections/projects/{projectId:guid}/database",
                "PUT api/v1/settings/connections/projects/{projectId:guid}/index",
                "PUT api/v1/settings/locality",
                "PUT api/v1/settings/menu",
            }
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(typeof(HostSettingsController))]
    [InlineData(typeof(ConnectionsSettingsController))]
    [InlineData(typeof(BackupSettingsController))]
    public void Legacy_controllers_keep_the_default_admin_policy(Type controllerType)
    {
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Policies.DefaultAdmin, authorize.Policy);
        Assert.Null(authorize.AuthenticationSchemes);
    }

    private static IEnumerable<string> Routes(Type controllerType)
    {
        var prefix = controllerType.GetCustomAttribute<RouteAttribute>()!.Template.Trim('/');
        return controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.HttpMethods.Select(method =>
                $"{method} {prefix}/{attribute.Template?.Trim('/')}".TrimEnd('/')));
    }
}
