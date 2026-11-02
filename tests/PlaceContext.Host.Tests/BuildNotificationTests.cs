using Microsoft.AspNetCore.Components;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class BuildNotificationTests
{
    [Fact]
    public void View_navigates_to_the_exact_link_and_closes_the_notification_panel()
    {
        var navigation = new Navigation();
        using var vm = new NotificationsViewModel(new OperationCenter(null!, null!), new FakeCurrentTenant(Guid.NewGuid()), navigation);
        var project = Guid.NewGuid(); var conversation = Guid.NewGuid();
        vm.Toggle();
        vm.Open($"/project/{project}/dashboard?conversation={conversation}");
        Assert.Equal($"http://localhost/project/{project}/dashboard?conversation={conversation}", navigation.Uri);
        Assert.False(vm.IsOpen);
        vm.Open("/observability?run=123");
        Assert.Equal("http://localhost/observability?run=123", navigation.Uri);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/jobs");
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
