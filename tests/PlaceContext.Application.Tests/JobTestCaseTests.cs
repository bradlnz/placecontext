using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Tests;

public sealed class JobTestCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Json_subset_allows_extra_object_properties()
    {
        var test = Test(JobTestAssertionType.JsonSubset, """{"customer":{"status":"active"}}""");
        var run = Run("""{"customer":{"status":"active","name":"Ada"},"traceId":"123"}""");

        var result = RunJobTestCaseHandler.Evaluate(
            test, run, RunJobChainHandler.PrimaryOutput(run));

        Assert.Equal("Passed", result.Status);
    }

    [Fact]
    public void Json_subset_reports_structural_mismatch()
    {
        var test = Test(JobTestAssertionType.JsonSubset, """{"status":"active"}""");
        var run = Run("""{"status":"inactive"}""");

        var result = RunJobTestCaseHandler.Evaluate(
            test, run, RunJobChainHandler.PrimaryOutput(run));

        Assert.Equal("Failed", result.Status);
        Assert.Contains("expected JSON structure", result.Message);
    }

    [Fact]
    public void Output_assertion_cannot_pass_when_job_failed()
    {
        var test = Test(JobTestAssertionType.OutputContains, "error");
        var run = Run("error", "Failed");

        var result = RunJobTestCaseHandler.Evaluate(test, run, "error");

        Assert.Equal("Failed", result.Status);
        Assert.Contains("finished as Failed", result.Message);
    }

    [Theory]
    [InlineData(JobTestAssertionType.OutputEquals, "hello", " hello ", "Passed")]
    [InlineData(JobTestAssertionType.OutputEquals, "hello", "hello!", "Failed")]
    [InlineData(JobTestAssertionType.OutputContains, "ell", "hello", "Passed")]
    public void Text_assertions_are_predictable(
        JobTestAssertionType assertion, string expected, string actual, string status)
    {
        var result = RunJobTestCaseHandler.Evaluate(Test(assertion, expected), Run(actual), actual);
        Assert.Equal(status, result.Status);
    }

    private static JobTestCaseRecord Test(JobTestAssertionType assertion, string? expected) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", null, assertion, expected,
        true, "NotRun", null, null, null, null, null, Now, Now);

    private static JobRunDetailView Run(string output, string status = "Succeeded") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, Now, Now.AddSeconds(1),
        new[]
        {
            new ShardResultView(0, status == "Succeeded" ? 0 : 1, status, output, null,
            Array.Empty<RunArtifactView>()),
        },
        null,
        new JobRunSnapshotView("code", "test.py", null, null, 1, 1, false));
}
