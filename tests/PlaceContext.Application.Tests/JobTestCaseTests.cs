using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

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

    [Fact]
    public async Task Test_owned_code_receives_job_result_and_controls_pass_status()
    {
        var projectId = Guid.NewGuid();
        var job = Job.Create(projectId, "customer lookup", null,
            new MapSpec("image", new[] { "{}" }, new Dictionary<string, string>
            {
                ["REAL_SERVICE_TOKEN"] = "must-not-enter-test",
            }),
            null, 1, ExitCodePolicy.Default, Now, allowNetworkEgress: true);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);
        var test = new JobTestCaseRecord(
            Guid.NewGuid(), projectId, job.Id, "active customer", """{"id":"123"}""",
            JobTestAssertionType.Succeeds, null, true, "NotRun", null, null,
            null, null, null, Now, Now, "python", "test.py",
            new[] { new CodeFileDto("test.py", "print('ok')") }, false);
        var store = new MemoryTestStore(test);
        var workloads = new FakeWorkloadRunner();
        workloads.EnqueueResult(new WorkloadRunResult(
            0, """{"status":"active"}""", "", ""));
        workloads.EnqueueResult(new WorkloadRunResult(0, null,
            JobTestFramework.ResultPrefix +
            """[{"name":"test_active","status":"Passed","durationMs":4},{"name":"test_shards","status":"Passed","durationMs":2}]""",
            ""));
        var handler = new RunJobTestCaseHandler(
            store, jobs, workloads, new FakeClock(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(new RunJobTestCaseCommand(test.Id));

        Assert.Equal("Passed", result.LastStatus);
        Assert.Equal("unittest: 2/2 passed.", result.LastMessage);
        Assert.Equal(2, result.MethodResults!.Count);
        Assert.All(result.MethodResults, method => Assert.Equal("Passed", method.Status));
        Assert.Null(result.LastJobRunId);
        Assert.Equal(2, workloads.ReceivedRequests.Count);
        var jobRequest = workloads.ReceivedRequests[0];
        Assert.Equal("image", jobRequest.Image);
        Assert.Empty(jobRequest.Env);
        Assert.False(jobRequest.AllowNetworkEgress);
        Assert.Equal("""{"id":"123"}""", jobRequest.StdinPayload);
        var validatorRequest = workloads.ReceivedRequests[1];
        Assert.Equal("python", validatorRequest.RuntimeId);
        Assert.Equal("_placecontext_test_runner.py", validatorRequest.Entrypoint);
        Assert.Contains(validatorRequest.CodeFiles!, file => file.Path == "test.py");
        Assert.Contains(validatorRequest.CodeFiles!, file => file.Path == "_placecontext_test_runner.py");
        Assert.Empty(validatorRequest.Env);
        Assert.False(validatorRequest.AllowNetworkEgress);
        using var stdin = System.Text.Json.JsonDocument.Parse(validatorRequest.StdinPayload);
        Assert.Equal("active", stdin.RootElement.GetProperty("run")
            .GetProperty("output").GetProperty("status").GetString());
        Assert.Equal("123", stdin.RootElement.GetProperty("input").GetProperty("id").GetString());
        var shard = stdin.RootElement.GetProperty("run").GetProperty("shards")[0];
        Assert.Equal(0, shard.GetProperty("index").GetInt32());
        Assert.Equal(0, shard.GetProperty("exitCode").GetInt32());
        Assert.Equal("Succeeded", shard.GetProperty("outcome").GetString());
        Assert.False(shard.TryGetProperty("Index", out _));
    }

    [Fact]
    public async Task Nonzero_test_code_exit_fails_the_test()
    {
        var projectId = Guid.NewGuid();
        var job = Job.Create(projectId, "lookup", null,
            new MapSpec("image", new[] { "{}" }, new Dictionary<string, string>()),
            null, 1, ExitCodePolicy.Default, Now);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);
        var test = new JobTestCaseRecord(
            Guid.NewGuid(), projectId, job.Id, "invalid result", null,
            JobTestAssertionType.Succeeds, null, true, "NotRun", null, null,
            null, null, null, Now, Now, "node", "test.js",
            new[] { new CodeFileDto("test.js", "process.exit(1)") }, false);
        var store = new MemoryTestStore(test);
        var workloads = new FakeWorkloadRunner();
        workloads.EnqueueResult(new WorkloadRunResult(0, "{}", "", ""));
        workloads.EnqueueResult(new WorkloadRunResult(7, null,
            JobTestFramework.ResultPrefix +
            """[{"name":"returns total","status":"Failed","durationMs":3,"message":"expected total 3"}]""",
            "expected total 3"));
        var handler = new RunJobTestCaseHandler(
            store, jobs, workloads, new FakeClock(Now));

        var result = await handler.HandleAsync(new RunJobTestCaseCommand(test.Id));

        Assert.Equal("Failed", result.LastStatus);
        Assert.Equal("Node test: 0/1 passed, 1 failed.", result.LastMessage);
        var method = Assert.Single(result.MethodResults!);
        Assert.Equal("Failed", method.Status);
        Assert.Equal("expected total 3", method.Message);
    }

    [Theory]
    [InlineData("python", "def test_customer():\n    pass", "test_customer")]
    [InlineData("node", "test('customer loads', () => {});", "customer loads")]
    [InlineData("go", "func TestCustomerLoads(t *testing.T) {}", "TestCustomerLoads")]
    [InlineData("ruby", "def test_customer_loads\nend", "test_customer_loads")]
    public void Framework_discovers_test_methods(
        string runtime, string source, string expectedName)
    {
        var methods = JobTestFramework.Discover(runtime, [new CodeFileDto("test", source)]);

        var method = Assert.Single(methods);
        Assert.Equal(expectedName, method.Name);
        Assert.Equal("NotRun", method.Status);
    }

    [Theory]
    [InlineData("python", "_placecontext_test_runner.py")]
    [InlineData("node", "_placecontext_test_runner.cjs")]
    [InlineData("go", "_placecontext_test_runner.go")]
    [InlineData("ruby", "_placecontext_test_runner.rb")]
    public void Framework_builds_an_isolated_runner(string runtime, string expectedEntrypoint)
    {
        var (runner, entrypoint) = JobTestFramework.BuildRunner(runtime, "user_test.py");

        Assert.Equal(expectedEntrypoint, entrypoint);
        Assert.Equal(expectedEntrypoint, runner.Path);
        Assert.Contains(JobTestFramework.ResultPrefix, runner.Content);
    }

    private static JobTestCaseRecord Test(JobTestAssertionType assertion, string? expected) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", null, assertion, expected,
        true, "NotRun", null, null, null, null, null, Now, Now,
        null, null, Array.Empty<CodeFileDto>(), false);

    private static JobRunDetailView Run(string output, string status = "Succeeded") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, Now, Now.AddSeconds(1),
        new[]
        {
            new ShardResultView(0, status == "Succeeded" ? 0 : 1, status, output, null,
            Array.Empty<RunArtifactView>()),
        },
        null,
        new JobRunSnapshotView("code", "test.py", null, null, 1, 1, false));

    private sealed class MemoryTestStore : IJobTestStore
    {
        private JobTestCaseRecord _test;
        public MemoryTestStore(JobTestCaseRecord test) => _test = test;
        public Task<JobTestCaseRecord?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<JobTestCaseRecord?>(_test.Id == id ? _test : null);
        public Task<IReadOnlyList<JobTestCaseRecord>> ListForProjectAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobTestCaseRecord>>(
                _test.ProjectId == projectId ? new[] { _test } : Array.Empty<JobTestCaseRecord>());
        public Task SaveAsync(JobTestCaseRecord test, CancellationToken ct = default)
        {
            _test = test;
            return Task.CompletedTask;
        }
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
