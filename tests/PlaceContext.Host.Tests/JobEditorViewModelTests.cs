using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobEditorViewModelTests
{
    [Fact]
    public void Job_editor_page_is_thin_and_uses_its_view_model()
    {
        var page = ReadHostSource("Components/Pages/JobEditor.razor");

        Assert.Contains("@inject JobEditorViewModel Vm", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Detach", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task SaveCoreAsync", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_code_editor_page_is_thin_and_uses_its_view_model()
    {
        var page = ReadHostSource("Components/Pages/JobTestCodeEditor.razor");

        Assert.Contains("@inject JobTestCodeEditorViewModel Vm", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Detach", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task SaveCoreAsync", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Components/Pages/JobEditor.razor")]
    [InlineData("Components/Pages/JobTestCodeEditor.razor")]
    public void Editor_views_do_not_reference_removed_component_state(string relativePath)
    {
        var page = ReadHostSource(relativePath);

        Assert.DoesNotContain("_test", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_monacoLite", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_message", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_running", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_saving", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_addingFile", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("_panelOpen", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Vm.Vm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultEntrypoint(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_views_delegate_state_changes_to_view_model_commands()
    {
        var jobPage = ReadHostSource("Components/Pages/JobEditor.razor");
        var testPage = ReadHostSource("Components/Pages/JobTestCodeEditor.razor");

        Assert.Contains("Vm.TogglePanel", jobPage, StringComparison.Ordinal);
        Assert.Contains("Vm.TogglePanel", testPage, StringComparison.Ordinal);
        Assert.Contains("Vm.SetEntry", testPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Vm.PanelOpen =", jobPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Vm.PanelOpen =", testPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Vm.Entrypoint =", testPage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("main.py", "python")]
    [InlineData("script.mjs", "javascript")]
    [InlineData("go.mod", "plaintext")]
    [InlineData("README.md", "markdown")]
    public void Editor_language_is_selected_from_the_file_extension(string path, string expected)
    {
        Assert.Equal(expected, EditorLanguageCatalog.ForPath(path));
    }

    [Theory]
    [InlineData(" \\folder\\file.py ", "folder/file.py")]
    [InlineData("/main.py", "main.py")]
    public void Editor_paths_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, EditorPathCatalog.Normalize(input));
    }

    [Theory]
    [InlineData("python", "test_job.py")]
    [InlineData("node", "job.test.js")]
    [InlineData("go", "job_test.go")]
    [InlineData("ruby", "job_test.rb")]
    public void Test_runtime_catalog_provides_named_starters(string runtime, string expectedPath)
    {
        var starter = JobTestRuntimeCatalog.Starter(runtime);

        Assert.Equal(expectedPath, starter.Path);
        Assert.NotEmpty(starter.Content);
        Assert.Equal(expectedPath, JobTestRuntimeCatalog.DefaultEntrypoint(runtime));
    }

    [Theory]
    [InlineData("Succeeded", "var(--good)", "var(--good-bg)")]
    [InlineData("Partial", "var(--warn)", "var(--warn-bg)")]
    [InlineData("Failed", "var(--bad)", "var(--bad-bg)")]
    public void Job_run_status_presentation_is_centralized(
        string status,
        string color,
        string background
    )
    {
        Assert.Equal(color, JobEditorViewModel.StatusColor(status));
        Assert.Equal(background, JobEditorViewModel.StatusBackground(status));
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
            {
                return File.ReadAllText(Path.Combine(host, relativePath));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
