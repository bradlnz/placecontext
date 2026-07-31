using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers;
using Pages = PlaceContext.Host.Components.Pages;

namespace PlaceContext.Host.Tests;

public sealed class SectionAuthorizationTests
{
    public static TheoryData<Type, string> SensitivePages => new()
    {
        { typeof(Pages.Dashboard), Permission.ProjectsView },
        { typeof(Pages.Overview), Permission.ProjectsView },
        { typeof(Pages.ProjectView), Permission.ProjectsView },
        { typeof(Pages.Onboarding), Permission.ProjectsView },
        { typeof(Pages.Crm), Permission.CrmView },
        { typeof(Pages.Artifacts), Permission.ArtifactsView },
        { typeof(Pages.Chat), Permission.AgentsChat },
        { typeof(Pages.DataEntities), Permission.DataRead },
        { typeof(Pages.DataMap), Permission.DataRead },
        { typeof(Pages.ProjectAnalytics), Permission.DataRead },
        { typeof(Pages.Events), Permission.EventsManage },
        { typeof(Pages.JobChains), Permission.ChainsManage },
        { typeof(Pages.Jobs), Permission.JobsView },
        { typeof(Pages.JobEditor), Permission.JobsEdit },
        { typeof(Pages.Schedules), Permission.TriggersManage },
        { typeof(Pages.Inspector), Permission.JobsView },
        { typeof(Pages.McpSettings), Permission.SettingsManage },
    };

    [Theory]
    [MemberData(nameof(SensitivePages))]
    public void Sensitive_pages_enforce_their_section_permission(Type page, string expectedPolicy)
        => AssertPolicy(page, expectedPolicy);

    public static TheoryData<Type, string> SensitiveControllers => new()
    {
        { typeof(ArtifactsController), Permission.ArtifactsView },
        { typeof(ChatAttachmentsController), Permission.AgentsChat },
        { typeof(CrmArtifactsController), Permission.CrmView },
    };

    [Theory]
    [MemberData(nameof(SensitiveControllers))]
    public void Sensitive_download_controllers_match_their_page_permission(
        Type controller, string expectedPolicy)
        => AssertPolicy(controller, expectedPolicy);

    [Fact]
    public void Mcp_settings_also_requires_secret_management_permission()
        => AssertPolicy(typeof(Pages.McpSettings), Permission.SecretsManage);

    private static void AssertPolicy(Type type, string expectedPolicy)
    {
        var attributes = type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();
        Assert.Contains(attributes, attribute => attribute.Policy == expectedPolicy);
    }
}
