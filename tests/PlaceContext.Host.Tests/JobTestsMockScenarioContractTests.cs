namespace PlaceContext.Host.Tests;

public sealed class JobTestsMockScenarioContractTests
{
    [Fact]
    public void Tests_use_declared_mock_scenarios_and_pytest_without_executing_jobs()
    {
        var testsPage = Source("src/PlaceContext.Host/Components/Pages/JobTests.razor");
        var codeEditor = Source("src/PlaceContext.Host/Components/Pages/JobTestCodeEditor.razor");
        var viewModel = Source(
            "src/PlaceContext.Host/Components/ViewModels/JobTestCodeEditorViewModel.cs"
        );
        var handler = Source("src/PlaceContext.Application/Jobs/Handlers/JobTestHandlers.cs");
        var framework = Source("src/PlaceContext.Application/Jobs/Services/JobTestFramework.cs");

        Assert.Contains("Mock scenario JSON", testsPage, StringComparison.Ordinal);
        Assert.Contains("Never executes the selected Job", testsPage, StringComparison.Ordinal);
        Assert.Contains(
            "Running framework tests against the mock scenario",
            testsPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "Passed to the Job as its single input payload",
            testsPage,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("Executing isolated Job code", testsPage, StringComparison.Ordinal);

        Assert.Contains("Python · pytest", codeEditor, StringComparison.Ordinal);
        Assert.Contains("requirements.txt", viewModel, StringComparison.Ordinal);
        Assert.Contains("available under <code>job/</code>", codeEditor, StringComparison.Ordinal);
        Assert.Contains("requirements.txt", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("import unittest", codeEditor, StringComparison.Ordinal);

        Assert.DoesNotContain("RunMockJobAsync", handler, StringComparison.Ordinal);
        Assert.Contains(
            "var scenario = ParseScenario(test.InputPayload);",
            handler,
            StringComparison.Ordinal
        );
        Assert.Contains("\"python\" => \"pytest\"", framework, StringComparison.Ordinal);
        Assert.Contains("pytest.main", framework, StringComparison.Ordinal);
    }

    private static string Source(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }
}
