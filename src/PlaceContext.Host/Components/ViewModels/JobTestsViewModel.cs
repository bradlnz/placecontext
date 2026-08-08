using System.Globalization;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobTestsViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;

    public JobTestsViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation
    ) => (_service, _ui, _navigation) = (service, ui, navigation);

    public Guid ProjectId { get; private set; }
    public IReadOnlyList<JobView> Jobs { get; private set; } = Array.Empty<JobView>();
    public List<JobTestCaseView> Tests { get; } = new();
    public HashSet<Guid> Running { get; } = new();
    public bool Loading { get; private set; } = true;
    public bool Editing { get; private set; }
    public bool Saving { get; private set; }
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public Guid? ConfirmDelete { get; set; }
    public Guid? EditId { get; private set; }
    public Guid EditJobId { get; set; }
    public string EditName { get; set; } = "";
    public string EditInput { get; set; } = "";
    public JobTestAssertionType EditAssertion { get; set; } = JobTestAssertionType.Succeeds;
    public string EditExpected { get; set; } = "";
    public bool EditEnabled { get; set; } = true;
    public IEnumerable<IGrouping<string, JobTestCaseView>> Suites =>
        Tests.GroupBy(test => test.JobName).OrderBy(group => group.Key);
    public List<JobTestCaseView> EnabledTests => Tests.Where(test => test.Enabled).ToList();
    public int TotalMethodCount => Tests.Sum(MethodCount);
    public int PassedCount =>
        Tests.SelectMany(MethodResults).Count(result => result.Status == JobTestStatuses.Passed);
    public int FailedCount =>
        Tests.SelectMany(MethodResults).Count(result => result.Status == JobTestStatuses.Failed);
    public int NotRunCount => Math.Max(0, TotalMethodCount - PassedCount - FailedCount);
    public int PassPercent =>
        TotalMethodCount == 0 ? 0 : (int)Math.Round(PassedCount * 100d / TotalMethodCount);
    public string ExpectedPlaceholder =>
        EditAssertion == JobTestAssertionType.JsonSubset
            ? "{\"status\":\"ok\"}"
            : "Expected output";
    public string AssertionHelp =>
        EditAssertion switch
        {
            JobTestAssertionType.JsonSubset =>
                "Object properties may be a subset; arrays are compared by position.",
            JobTestAssertionType.OutputContains =>
                "Passes when the primary output contains this exact text.",
            _ => "Passes when the trimmed primary output is exactly this value.",
        };
    public const string DefaultScenario =
        "{\n  \"input\": {\"customerId\":\"example\"},\n  \"run\": {\"status\": \"Succeeded\", \"output\": {\"status\":\"active\"}, \"shards\": []}\n}";

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        _ui.Set("Tests", "verify Job code");
        Loading = true;
        try
        {
            var jobs = _service.ListJobsAsync(projectId);
            var tests = _service.ListJobTestCasesAsync(projectId);
            await Task.WhenAll(jobs, tests);
            Jobs = await jobs;
            Tests.Clear();
            Tests.AddRange(await tests);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public void NewTest()
    {
        EditId = null;
        EditJobId = Jobs.FirstOrDefault()?.Id ?? Guid.Empty;
        EditName = "";
        EditInput = DefaultScenario;
        EditAssertion = JobTestAssertionType.Succeeds;
        EditExpected = "";
        EditEnabled = true;
        Editing = true;
    }

    public void EditTest(JobTestCaseView test)
    {
        EditId = test.Id;
        EditJobId = test.JobId;
        EditName = test.Name;
        EditInput = test.InputPayload ?? "";
        EditAssertion = test.AssertionType;
        EditExpected = test.ExpectedValue ?? "";
        EditEnabled = test.Enabled;
        Editing = true;
    }

    public void CloseEditor() => Editing = false;

    public void OpenCodeEditor(Guid testId) =>
        _navigation.NavigateTo($"/project/{ProjectId}/tests/{testId}");

    public void OpenJobs() => _navigation.NavigateTo($"/project/{ProjectId}/jobs");

    public async Task SaveAsync()
    {
        if (EditJobId == Guid.Empty || string.IsNullOrWhiteSpace(EditName))
        {
            ShowMessage("Choose a Job and enter a block name.", true);
            return;
        }
        if (
            EditAssertion != JobTestAssertionType.Succeeds
            && string.IsNullOrWhiteSpace(EditExpected)
        )
        {
            ShowMessage("Enter the expected value for this assertion.", true);
            return;
        }
        Saving = true;
        try
        {
            Replace(
                await _service.SaveJobTestCaseAsync(
                    new SaveJobTestCaseCommand(
                        ProjectId,
                        EditJobId,
                        EditName,
                        EditInput,
                        EditAssertion,
                        EditExpected,
                        EditEnabled,
                        EditId
                    )
                )
            );
            Editing = false;
            ShowMessage("Saved block.");
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            Saving = false;
            NotifyStateChanged();
        }
    }

    public async Task RunAsync(Guid id)
    {
        if (!Running.Add(id))
            return;
        ConfirmDelete = null;
        try
        {
            Replace(await _service.RunJobTestCaseAsync(id));
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            Running.Remove(id);
            NotifyStateChanged();
        }
    }

    public async Task RunAllAsync()
    {
        foreach (var test in EnabledTests)
            await RunAsync(test.Id);
        ShowMessage(
            FailedCount == 0
                ? $"All {EnabledTests.Sum(MethodCount)} enabled test methods passed."
                : $"{FailedCount} test methods failed.",
            FailedCount > 0
        );
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            if (await _service.DeleteJobTestCaseAsync(id))
            {
                Tests.RemoveAll(test => test.Id == id);
                ShowMessage("Test block deleted.");
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, true);
        }
        finally
        {
            ConfirmDelete = null;
            NotifyStateChanged();
        }
    }

    private void Replace(JobTestCaseView updated)
    {
        var index = Tests.FindIndex(test => test.Id == updated.Id);
        if (index < 0)
            Tests.Add(updated);
        else
            Tests[index] = updated;
    }

    private void ShowMessage(string message, bool error = false)
    {
        Message = message;
        MessageIsError = error;
    }

    public static string StatusClass(JobTestCaseView test, bool running) =>
        running ? "running" : test.LastStatus.ToLowerInvariant();

    public static string StatusLabel(JobTestCaseView test, bool running) =>
        running ? "Running"
        : test.LastStatus == "NotRun" ? "Not run"
        : test.LastStatus;

    public static IReadOnlyList<JobTestMethodResult> MethodResults(JobTestCaseView test) =>
        test.MethodResults is { Count: > 0 } results
            ? results
            : JobTestFramework.Discover(test.RuntimeId, test.CodeFiles);

    public static int MethodCount(JobTestCaseView test) => MethodResults(test).Count;

    public static string AssertionLabel(JobTestAssertionType assertion) =>
        assertion switch
        {
            JobTestAssertionType.Succeeds => "Run succeeds",
            JobTestAssertionType.OutputEquals => "Output equals",
            JobTestAssertionType.OutputContains => "Output contains",
            JobTestAssertionType.JsonSubset => "JSON subset",
            _ => assertion.ToString(),
        };

    public static string FormatDuration(long milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds} ms" : $"{milliseconds / 1000d:0.0} s";

    public string DurationLabel(long milliseconds) => FormatDuration(milliseconds);

    public string StatusClassLabel(JobTestCaseView test, bool running) =>
        StatusClass(test, running);

    public string StatusLabelText(JobTestCaseView test, bool running) => StatusLabel(test, running);

    public static bool IsPassed(JobTestCaseView test) => test.LastStatus == "Passed";

    public static bool IsFailed(JobTestCaseView test) => test.LastStatus == "Failed";

    public static bool HasFailureOutput(JobTestCaseView test, bool running) =>
        !running && IsFailed(test) && !string.IsNullOrWhiteSpace(test.LastActualOutput);

    public static int SuitePassedCount(IEnumerable<JobTestCaseView> suite) =>
        suite.SelectMany(MethodResults).Count(result => result.Status == "Passed");
}
