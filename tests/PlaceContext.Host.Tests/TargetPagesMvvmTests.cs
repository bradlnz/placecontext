using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Wiki;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tests;

public sealed class TargetPagesMvvmArchitectureTests
{
    [Theory]
    [InlineData("About.razor", "AboutViewModel")]
    [InlineData("Onboarding.razor", "OnboardingViewModel")]
    [InlineData("Setup.razor", "SetupViewModel")]
    [InlineData("Wiki.razor", "WikiViewModel")]
    [InlineData("LocalitySettings.razor", "LocalitySettingsViewModel")]
    public void Target_view_uses_its_view_model_and_has_no_domain_service_logic(
        string pageName,
        string viewModelName
    )
    {
        var page = ReadHostSource($"Components/Pages/{pageName}");

        Assert.Contains($"@inject {viewModelName} Vm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject NavigationManager", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IAuthService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject ITenantStore", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject ICurrentTenant", page, StringComparison.Ordinal);
        Assert.DoesNotContain("WikiLibrary", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeZoneInfo", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private string", page, StringComparison.Ordinal);
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}

public sealed class TargetPageViewModelTests
{
    [Fact]
    public void About_exposes_current_copyright_year_and_sets_shell_state()
    {
        var ui = new PortalUiState();
        var vm = new AboutViewModel(
            ui,
            new FixedTimeProvider(new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero))
        );

        vm.Initialize();

        Assert.Equal(2031, vm.CopyrightYear);
        Assert.Equal("About", ui.Title);
    }

    [Fact]
    public void Onboarding_routes_each_source_to_its_existing_setup_surface()
    {
        var projectId = Guid.NewGuid();

        Assert.Equal(PageRoutes.ProjectData(projectId),
            OnboardingViewModel.DataSourceRoute("workspace-database", projectId));
        Assert.Equal(PageRoutes.ConnectionsSettings,
            OnboardingViewModel.DataSourceRoute("postgresql", projectId));
        Assert.Equal(PageRoutes.McpSettings,
            OnboardingViewModel.DataSourceRoute("mcp", projectId));
        Assert.Equal(PageRoutes.WebhookIngestionWiki,
            OnboardingViewModel.DataSourceRoute("webhook", projectId));
        Assert.Equal(PageRoutes.ProjectDataJobs(projectId),
            OnboardingViewModel.DataSourceRoute("job", projectId));
    }

    [Fact]
    public async Task Setup_loads_configured_state_and_redirects_to_login()
    {
        var nav = new TestNavigationManager();
        var vm = new SetupViewModel(new StubAuthService(isUnconfigured: false), nav);

        await vm.InitializeAsync();

        Assert.True(vm.Configured);
        Assert.Equal(PageRoutes.Login, nav.NavigatedUri);
        Assert.True(nav.ForceLoad);
    }

    [Fact]
    public void Wiki_selects_default_or_requested_article_and_closes_contents()
    {
        var vm = new WikiViewModel(new PortalUiState());

        vm.SetParameters(null);
        Assert.Equal(WikiLibrary.Articles[0], vm.Article);

        vm.ToggleContents();
        Assert.True(vm.TocOpen);
        vm.CloseContents();
        Assert.False(vm.TocOpen);
    }

    [Fact]
    public async Task Locality_rejects_unknown_timezone_without_persisting()
    {
        var tenants = new StubTenantStore();
        var tenant = new StubCurrentTenant();
        var nav = new TestNavigationManager();
        var vm = new LocalitySettingsViewModel(tenants, tenant, new PortalUiState(), nav);
        vm.TimeZoneId = "not-a-timezone";

        await vm.SaveAsync();

        Assert.Equal("Unknown timezone id 'not-a-timezone'.", vm.Message);
        Assert.False(tenants.Saved);
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public string? NavigatedUri { get; private set; }
        public bool Replace { get; private set; }
        public bool ForceLoad { get; private set; }

        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            NavigatedUri = uri;
            Replace = options.ReplaceHistoryEntry;
            ForceLoad = options.ForceLoad;
        }
    }

    private sealed class StubAuthService(bool isUnconfigured) : IAuthService
    {
        public Task<bool> IsUnconfiguredAsync(CancellationToken ct = default) =>
            Task.FromResult(isUnconfigured);

        public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> HasAnyMembersAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<AuthUser?> RegisterAsync(
            string email,
            string displayName,
            string password,
            UserRole role,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<AuthUser> GetOrCreateExternalUserAsync(
            string email,
            string displayName,
            UserRole defaultRole,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<AuthUser> GetOrCreateOperatorAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<AuthUser?> CreateFirstAdminAsync(
            string email,
            string displayName,
            string password,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<AuthUser?> ValidateCredentialsAsync(
            string email,
            string password,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<bool> IsTwoFactorRequiredAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<bool> IsTwoFactorEnabledAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TwoFactorDeliveryInfo> GetTwoFactorDeliveryInfoAsync(
            Guid userId,
            string? channel = null,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TwoFactorSettingsInfo> GetTwoFactorSettingsAsync(
            Guid userId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TwoFactorChallenge> IssueTwoFactorCodeAsync(
            Guid userId,
            string? channel = null,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task SetTwoFactorPhoneNumberAsync(
            Guid userId,
            string? phoneNumber,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task SetTwoFactorChannelAsync(
            Guid userId,
            string channel,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<bool> ConfirmTwoFactorSetupAsync(
            Guid userId,
            string code,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<bool> VerifyTwoFactorCodeAsync(
            Guid userId,
            string code,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<bool> DisableTwoFactorAsync(
            Guid userId,
            string currentCode,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class StubTenantStore : ITenantStore
    {
        public bool Saved { get; private set; }

        public Task SetTimeZoneAsync(
            Guid tenantId,
            string timeZoneId,
            CancellationToken ct = default
        )
        {
            Saved = true;
            return Task.CompletedTask;
        }

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TenantInfo> GetOrCreateAsync(string slug, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TenantRow?> GetRowAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task SaveGitHubAsync(
            Guid tenantId,
            string githubLogin,
            string accessToken,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<TenantInfo>> ListTenantsAsync(
            int take = 1000,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantInfo>>(Array.Empty<TenantInfo>());
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }
}
