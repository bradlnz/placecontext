namespace PlaceContext.Host.Tests;

public sealed class MvvmArchitectureTests
{
    [Fact]
    public void Schedules_view_delegates_state_and_commands_to_its_view_model()
    {
        var page = ReadHostSource("Components/Pages/Schedules.razor");

        Assert.Contains("@inject SchedulesViewModel Vm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PortalUiState", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject ICurrentTenant", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTriggerAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateTriggerAsync", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteTriggerAsync", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Notifications_bell_delegates_state_and_commands_to_its_view_model()
    {
        var view = ReadHostSource("Components/Shared/NotificationsBell.razor");

        Assert.Contains("@inject NotificationsViewModel Vm", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@inject PlaceContext.Infrastructure.Operations.OperationCenter",
            view,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "@inject PlaceContext.Application.Ports.ICurrentTenant",
            view,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("@inject NavigationManager", view, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private IReadOnlyList<PortalOperation>",
            view,
            StringComparison.Ordinal
        );
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
