using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers;
using Pages = PlaceContext.Host.Components.Pages;

namespace PlaceContext.Host.Tests;

public sealed class SectionAuthorizationTests
{
    public static TheoryData<Type, string> SensitivePages =>
        new()
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
        };

    [Theory]
    [MemberData(nameof(SensitivePages))]
    public void Sensitive_pages_enforce_their_section_permission(
        Type page,
        string expectedPolicy
    ) => AssertPolicy(page, expectedPolicy);

    // Every /settings/* page is default-admin-only, except the self-service API tokens page which
    // keeps a bare [Authorize] (any authenticated member).
    public static TheoryData<Type> DefaultAdminPages =>
        new()
        {
            typeof(Pages.AccessSettings),
            typeof(Pages.ArtifactSettings),
            typeof(Pages.BackupSettings),
            typeof(Pages.BrandingSettings),
            typeof(Pages.CommunicationsSettings),
            typeof(Pages.LocalitySettings),
            typeof(Pages.McpSettings),
            typeof(Pages.MenuSettings),
        };

    [Theory]
    [MemberData(nameof(DefaultAdminPages))]
    public void Settings_pages_require_the_default_admin(Type page) =>
        AssertPolicy(page, Policies.DefaultAdmin);

    public static TheoryData<Type> SelfServiceSettingsPages =>
        new() { typeof(Pages.ApiTokensSettings) };

    [Theory]
    [MemberData(nameof(SelfServiceSettingsPages))]
    public void Self_service_settings_pages_stay_open_to_any_member(Type page)
    {
        var attributes = page.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();
        Assert.Contains(attributes, attribute => attribute.Policy is null);
        Assert.DoesNotContain(attributes, attribute => attribute.Policy == Policies.DefaultAdmin);
    }

    public static TheoryData<Type, string> SensitiveControllers =>
        new()
        {
            { typeof(ArtifactsController), Permission.ArtifactsView },
            { typeof(ChatAttachmentsController), Permission.AgentsChat },
            { typeof(CrmArtifactsController), Permission.CrmView },
        };

    [Theory]
    [MemberData(nameof(SensitiveControllers))]
    public void Sensitive_download_controllers_match_their_page_permission(
        Type controller,
        string expectedPolicy
    ) => AssertPolicy(controller, expectedPolicy);

    // Controllers backing default-admin-only settings pages are gated by the same policy.
    public static TheoryData<Type> DefaultAdminControllers =>
        new()
        {
            typeof(BackupController),
            typeof(CommunicationProvidersController),
            typeof(JobMcpController),
        };

    [Theory]
    [MemberData(nameof(DefaultAdminControllers))]
    public void Settings_controllers_require_the_default_admin(Type controller) =>
        AssertPolicy(controller, Policies.DefaultAdmin);

    private static void AssertPolicy(Type type, string expectedPolicy)
    {
        var attributes = type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .ToList();
        Assert.Contains(attributes, attribute => attribute.Policy == expectedPolicy);
    }
}
