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
