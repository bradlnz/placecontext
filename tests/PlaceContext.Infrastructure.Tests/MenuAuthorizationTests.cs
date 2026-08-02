using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Tests;

public sealed class MenuAuthorizationTests
{
    [Theory]
    [InlineData("dashboard", Permission.ProjectsView)]
    [InlineData("crm", Permission.CrmView)]
    [InlineData("jobs", Permission.JobsView)]
    [InlineData("chains", Permission.ChainsManage)]
    [InlineData("schedules", Permission.TriggersManage)]
    [InlineData("data", Permission.DataRead)]
    [InlineData("data.tables", Permission.DataRead)]
    [InlineData("data.analytics", Permission.DataRead)]
    [InlineData("data.search", Permission.DataRead)]
    [InlineData("data.datamap", Permission.DataRead)]
    [InlineData("data.entities", Permission.DataRead)]
    [InlineData("project.events", Permission.EventsManage)]
    [InlineData("chat", Permission.AgentsChat)]
    [InlineData("artifacts", Permission.ArtifactsView)]
    public void Menu_item_uses_the_same_section_permission_as_its_route(
        string itemId, string expectedPermission)
    {
        var item = Assert.Single(
            MenuConfigService.WorkspaceCatalog, item => item.Id == itemId);

        Assert.Equal(expectedPermission, item.RequiredPermission);
    }
}
