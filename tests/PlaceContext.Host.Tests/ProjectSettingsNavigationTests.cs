using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Tenancy;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ProjectSettingsNavigationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Events_use_the_selected_project_and_require_their_own_permission(bool permitted, bool selected)
    {
        var projectId = Guid.NewGuid();
        var ui = new PortalUiState();
        if (selected) ui.SetProject(projectId, "Project");
        var identity = new Identity(permitted);
        using var services = new ServiceCollection().AddSingleton<IPermissionService>(identity).BuildServiceProvider();
        var vm = new SettingsLayoutViewModel(new Navigation($"/project/{projectId}/events"),
            services.GetRequiredService<IServiceScopeFactory>(), identity, ui);
        await vm.InitializeAsync();

        Assert.Equal(permitted && selected, vm.VisibleItems.Any(item => item.Path == "events"));
        Assert.False(vm.IsDefaultAdmin);
        if (permitted && selected)
        {
            Assert.Equal("Project", vm.VisibleItems.Single(item => item.Path == "events").Group);
            Assert.Equal($"/project/{projectId}/events", vm.Href("events"));
            Assert.True(vm.IsActive("events"));
            Assert.Equal("Events", vm.ActiveLabel);
            var nextProject = Guid.NewGuid();
            ui.SetProject(nextProject, "Next project");
            Assert.Equal($"/project/{nextProject}/events", vm.Href("events"));
            Assert.False(vm.IsActive("events"));
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string path) => Initialize("http://localhost/", "http://localhost" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class Identity(bool permitted) : ICurrentUser, IPermissionService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => "Member";
        public bool IsAuthenticated => true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) =>
            Task.FromResult(permitted && permission == Permission.EventsManage);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(permitted ? new HashSet<string> { Permission.EventsManage } : new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }
}
