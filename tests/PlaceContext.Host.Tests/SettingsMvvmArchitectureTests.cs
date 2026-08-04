namespace PlaceContext.Host.Tests;

public sealed class SettingsMvvmArchitectureTests
{
    private static readonly string[] Pages =
    [
        "BrandingSettings",
        "BackupSettings",
        "Secrets",
        "ApiTokensSettings",
        "ArtifactSettings",
        "MenuSettings",
        "McpSettings",
        "AccessSettings",
        "CommunicationsSettings",
    ];

    [Fact]
    public void Settings_pages_use_their_focused_view_models_and_keep_only_view_wiring()
    {
        foreach (var pageName in Pages)
        {
            var page = ReadHostSource($"Components/Pages/{pageName}.razor");
            var viewModelName = $"{pageName}ViewModel";

            Assert.Contains($"@inject {viewModelName} Vm", page, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject PortalUiState", page, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject NavigationManager", page, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
            Assert.DoesNotContain("private async Task", page, StringComparison.Ordinal);
            Assert.DoesNotContain("private IReadOnlyList", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Settings_view_models_are_page_view_models_and_contain_no_blazor_components()
    {
        foreach (var pageName in Pages)
        {
            var viewModel = ReadHostSource(
                $"Components/ViewModels/Settings/{pageName}ViewModel.cs"
            );

            Assert.Contains(": PageViewModel", viewModel, StringComparison.Ordinal);
            Assert.DoesNotContain("ComponentBase", viewModel, StringComparison.Ordinal);
        }
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
