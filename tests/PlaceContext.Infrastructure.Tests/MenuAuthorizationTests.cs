using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Tests;

public sealed class MenuAuthorizationTests
{
    [Fact]
    public void Hosted_apps_and_databases_are_not_part_of_the_workflow_navigation()
    {
        Assert.DoesNotContain(MenuConfigService.WorkspaceCatalog, item => item.Id is "services" or "databases");
        Assert.Equal("Dashboard", Assert.Single(MenuConfigService.WorkspaceCatalog, item => item.Id == "build.chat").DefaultLabel);
        Assert.DoesNotContain(MenuConfigService.WorkspaceCatalog, item => item.Id == "dashboard");
        Assert.Contains(MenuConfigService.WorkspaceCatalog, item => item.Id == "chains");
    }

    [Fact]
    public void Related_items_share_a_group_without_changing_their_routes_or_order()
    {
        var catalog = MenuConfigService.WorkspaceCatalog;
        var items = catalog.Select((item, index) => new ResolvedMenuItem(item.Id, item.DefaultLabel,
            item.Kind, item.HrefTemplate, item.Icon, item.DefaultSection, index)).ToArray();
        var grouped = MenuConfigService.GroupSections(items);
        var infrastructure = Assert.Single(grouped, item => item.Label == "Infrastructure");
        Assert.Equal(new[] { "queues", "networking", "vault", "cluster" },
            grouped.Where(item => item.Parent == infrastructure.Id).Select(item => item.Id));
        foreach (var item in items)
            Assert.Equal(item.Href, Assert.Single(grouped, resolved => resolved.Id == item.Id).Href);
        Assert.Null(Assert.Single(grouped, item => item.Id == "build.chat").Parent);
    }

    [Fact]
    public void Grouping_only_uses_visible_items_and_preserves_custom_labels_and_sections()
    {
        ResolvedMenuItem item = new("queues", "Incoming work", "link", "/project/test/queues", "queue", "Custom operations", 20);
        var grouped = MenuConfigService.GroupSections([item]);
        var group = Assert.Single(grouped, item => item.Kind == "group");
        Assert.Equal("Custom operations", group.Label);
        Assert.Equal(item with { Parent = group.Id }, Assert.Single(grouped, item => item.Kind == "link"));
        Assert.Empty(MenuConfigService.GroupSections([]));
    }

    [Theory]
    [InlineData("build.chat", Permission.ProjectsView)]
    [InlineData("jobs", Permission.JobsView)]
    [InlineData("chains", Permission.ChainsManage)]
    [InlineData("schedules", Permission.TriggersManage)]
    [InlineData("data", Permission.DataRead)]
    [InlineData("project.events", Permission.EventsManage)]
    [InlineData("artifacts", Permission.ArtifactsView)]
    public void Menu_item_uses_the_same_section_permission_as_its_route(
        string itemId, string expectedPermission)
    {
        var item = Assert.Single(
            MenuConfigService.WorkspaceCatalog, item => item.Id == itemId);

        Assert.Equal(expectedPermission, item.RequiredPermission);
    }
}
