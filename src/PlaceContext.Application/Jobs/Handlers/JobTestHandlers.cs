using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SaveJobTestCaseHandler
    : ICommandHandler<SaveJobTestCaseCommand, JobTestCaseView>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;
    private readonly IClock _clock;

    public SaveJobTestCaseHandler(IJobTestStore tests, IJobRepository jobs, IClock clock)
        => (_tests, _jobs, _clock) = (tests, jobs, clock);

    public async Task<JobTestCaseView> HandleAsync(
        SaveJobTestCaseCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Test name is required.", nameof(command));

        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");
        if (job.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The test and job must belong to the same project.");

        var now = _clock.UtcNow;
        var existing = command.TestId is { } id ? await _tests.GetAsync(id, ct) : null;
        if (command.TestId is not null && existing is null)
            throw new InvalidOperationException($"Test {command.TestId} not found.");
        if (existing is not null && existing.ProjectId != command.ProjectId)
            throw new InvalidOperationException("Test does not belong to this project.");

        var input = NullIfWhiteSpace(command.InputPayload);
        var expected = command.AssertionType == JobTestAssertionType.Succeeds
            ? null
            : NullIfWhiteSpace(command.ExpectedValue);
        var definitionChanged = existing is not null
            && (existing.JobId != command.JobId
                || existing.InputPayload != input
                || existing.AssertionType != command.AssertionType
                || existing.ExpectedValue != expected);
        var record = new JobTestCaseRecord(
            existing?.Id ?? Guid.NewGuid(),
            command.ProjectId,
            command.JobId,
            command.Name.Trim(),
            input,
            command.AssertionType,
            expected,
            command.Enabled,
            definitionChanged ? "NotRun" : existing?.LastStatus ?? "NotRun",
            definitionChanged ? null : existing?.LastMessage,
            definitionChanged ? null : existing?.LastActualOutput,
            definitionChanged ? null : existing?.LastJobRunId,
            definitionChanged ? null : existing?.LastRunAt,
            definitionChanged ? null : existing?.LastDurationMs,
            existing?.CreatedAt ?? now,
            now,
            existing?.RuntimeId,
            existing?.Entrypoint,
            existing?.CodeFiles ?? Array.Empty<CodeFileDto>(),
            existing?.AllowNetworkEgress ?? false,
            definitionChanged ? Array.Empty<JobTestMethodResult>() : existing?.MethodResults);

        await _tests.SaveAsync(record, ct);
        return ToView(record, job.Name);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static JobTestCaseView ToView(JobTestCaseRecord test, string jobName) => new(
        test.Id, test.ProjectId, test.JobId, jobName, test.Name, test.InputPayload,
        test.AssertionType, test.ExpectedValue, test.Enabled, test.LastStatus,
        test.LastMessage, test.LastActualOutput, test.LastJobRunId, test.LastRunAt,
        test.LastDurationMs, test.CreatedAt, test.UpdatedAt, test.RuntimeId,
        test.Entrypoint, test.CodeFiles, test.AllowNetworkEgress, test.MethodResults);
}

public sealed class DeleteJobTestCaseHandler : ICommandHandler<DeleteJobTestCaseCommand, bool>
{
    private readonly IJobTestStore _tests;
    public DeleteJobTestCaseHandler(IJobTestStore tests) => _tests = tests;

    public Task<bool> HandleAsync(DeleteJobTestCaseCommand command, CancellationToken ct = default)
        => _tests.DeleteAsync(command.TestId, ct);
}

public sealed class ListJobTestCasesHandler
    : IQueryHandler<ListJobTestCasesQuery, IReadOnlyList<JobTestCaseView>>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;

    public ListJobTestCasesHandler(IJobTestStore tests, IJobRepository jobs)
        => (_tests, _jobs) = (tests, jobs);

    public async Task<IReadOnlyList<JobTestCaseView>> HandleAsync(
        ListJobTestCasesQuery query, CancellationToken ct = default)
    {
        var jobs = (await _jobs.ListForProjectAsync(query.ProjectId, ct))
            .ToDictionary(job => job.Id, job => job.Name);
        return (await _tests.ListForProjectAsync(query.ProjectId, ct))
            .Select(test => SaveJobTestCaseHandler.ToView(
                test, jobs.GetValueOrDefault(test.JobId, "Deleted job")))
            .ToList();
    }
}

public sealed class GetJobTestCaseHandler
    : IQueryHandler<GetJobTestCaseQuery, JobTestCaseView?>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;

    public GetJobTestCaseHandler(IJobTestStore tests, IJobRepository jobs)
        => (_tests, _jobs) = (tests, jobs);

    public async Task<JobTestCaseView?> HandleAsync(
        GetJobTestCaseQuery query, CancellationToken ct = default)
    {
        var test = await _tests.GetAsync(query.TestId, ct);
        if (test is null) return null;
        var job = await _jobs.GetByIdAsync(test.JobId, ct);
        return SaveJobTestCaseHandler.ToView(test, job?.Name ?? "Deleted job");
    }
}

public sealed class UpdateJobTestCodeHandler
    : ICommandHandler<UpdateJobTestCodeCommand, JobTestCaseView>
{
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;
    private readonly IClock _clock;

    public UpdateJobTestCodeHandler(IJobTestStore tests, IJobRepository jobs, IClock clock)
        => (_tests, _jobs, _clock) = (tests, jobs, clock);

    public async Task<JobTestCaseView> HandleAsync(
        UpdateJobTestCodeCommand command, CancellationToken ct = default)
    {
        var test = await _tests.GetAsync(command.TestId, ct)
            ?? throw new InvalidOperationException($"Test {command.TestId} not found.");
        var job = await _jobs.GetByIdAsync(test.JobId, ct)
            ?? throw new InvalidOperationException($"Job {test.JobId} not found.");

        var source = WorkloadSourceResolver.ResolveRequired(
            null, command.RuntimeId, null, command.Entrypoint,
            command.CodeFiles, "Test code");
        var code = (Domain.ValueObjects.WorkloadSource.CodeWorkload)source;
        var updated = test with
        {
            RuntimeId = code.RuntimeId,
            Entrypoint = code.Entrypoint,
            CodeFiles = code.Files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList(),
            AllowNetworkEgress = false,
            LastStatus = "NotRun",
            LastMessage = null,
            LastActualOutput = null,
            LastJobRunId = null,
            LastRunAt = null,
            LastDurationMs = null,
            MethodResults = JobTestFramework.Discover(code.RuntimeId,
                code.Files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList()),
            UpdatedAt = _clock.UtcNow,
        };
        await _tests.SaveAsync(updated, ct);
        return SaveJobTestCaseHandler.ToView(updated, job.Name);
    }
}

public sealed class RunJobTestCaseHandler
    : ICommandHandler<RunJobTestCaseCommand, JobTestCaseView>
{
    private const int MaxStoredOutputLength = 65_536;
    private readonly IJobTestStore _tests;
    private readonly IJobRepository _jobs;
    private readonly IWorkloadRunner _workloads;
    private readonly IClock _clock;

    public RunJobTestCaseHandler(
        IJobTestStore tests,
        IJobRepository jobs,
        IWorkloadRunner workloads,
        IClock clock)
    {
        (_tests, _jobs, _workloads, _clock)
            = (tests, jobs, workloads, clock);
    }

    public async Task<JobTestCaseView> HandleAsync(
        RunJobTestCaseCommand command, CancellationToken ct = default)
    {
        var test = await _tests.GetAsync(command.TestId, ct)
            ?? throw new InvalidOperationException($"Test {command.TestId} not found.");
        var job = await _jobs.GetByIdAsync(test.JobId, ct)
            ?? throw new InvalidOperationException($"Job {test.JobId} not found.");

        var stopwatch = Stopwatch.StartNew();
        string? actual = null;
        IReadOnlyList<JobTestMethodResult> methodResults =
            test.MethodResults ?? Array.Empty<JobTestMethodResult>();
        string status;
        string message;

        try
        {
            var scenario = ParseScenario(test.InputPayload);
            actual = ScenarioOutput(scenario);
            if (test.CodeFiles.Count > 0)
            {
                (status, message, methodResults) = await RunTestCodeAsync(test, job, scenario, ct);
            }
            else
            {
                (status, message) = Evaluate(test, ScenarioStatus(scenario), actual);
                methodResults = new[] { new JobTestMethodResult(test.Name, status, null, message) };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = "Failed";
            message = $"Test execution error: {ex.Message}";
        }

        stopwatch.Stop();
        var completed = test with
        {
            LastStatus = status,
            LastMessage = message,
            LastActualOutput = Truncate(actual),
            // Mocked Job executions are intentionally not persisted in job_runs.
            LastJobRunId = null,
            LastRunAt = _clock.UtcNow,
            LastDurationMs = stopwatch.ElapsedMilliseconds,
            MethodResults = methodResults,
            UpdatedAt = _clock.UtcNow,
        };
        await _tests.SaveAsync(completed, ct);
        return SaveJobTestCaseHandler.ToView(completed, job.Name);
    }

    private async Task<(string Status, string Message, IReadOnlyList<JobTestMethodResult> Methods)> RunTestCodeAsync(
        JobTestCaseRecord test,
        Job job,
        JsonObject scenario,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(test.RuntimeId))
            return ("Failed", "Test block has no framework selected.", Array.Empty<JobTestMethodResult>());

        var target = test.Entrypoint ?? test.CodeFiles.FirstOrDefault()?.Path;
        if (string.IsNullOrWhiteSpace(target))
            return ("Failed", "Test block has no test entrypoint.", Array.Empty<JobTestMethodResult>());
        var (runner, runnerEntrypoint) = JobTestFramework.BuildRunner(test.RuntimeId, target);
        var files = test.CodeFiles
            .Where(file => !string.Equals(file.Path, runner.Path, StringComparison.Ordinal))
            .Append(runner)
            .ToList();
        AppendJobSource(files, job.MapSpec.Source, "job");
        if (job.ReduceSpec is { } reduce)
            AppendJobSource(files, reduce.Source, "job/reduce");
        if (string.Equals(test.RuntimeId, "python", StringComparison.Ordinal))
            EnsurePytestDependency(files);

        var stdin = scenario.ToJsonString();
        var result = await _workloads.RunAsync(new WorkloadRunRequest(
            Image: null,
            StdinPayload: stdin,
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: $"job-test-{test.Id:N}-{Guid.NewGuid():N}",
            CodeFiles: files.Select(file => (file.Path, file.Content)).ToList(),
            RuntimeId: test.RuntimeId,
            Entrypoint: runnerEntrypoint,
            AllowNetworkEgress: false,
            TimeoutSeconds: 300), ct);

        var methods = JobTestFramework.ParseResults(result.Stdout, result.Stderr, result.Artifact);
        if (methods.Count == 0)
        {
            var output = FirstNonBlank(result.Stderr, result.Stdout, result.Artifact);
            return ("Failed",
                $"{JobTestFramework.Label(test.RuntimeId)} did not report any test methods: "
                + TruncateMessage(output ?? "No output."),
                methods);
        }

        var failed = methods.Count(method => method.Status == "Failed");
        var skipped = methods.Count(method => method.Status == "Skipped");
        var passed = methods.Count - failed - skipped;
        var status = result.ExitCode == 0 && failed == 0 ? "Passed" : "Failed";
        var summary = $"{JobTestFramework.Label(test.RuntimeId)}: {passed}/{methods.Count} passed";
        if (skipped > 0) summary += $", {skipped} skipped";
        if (failed > 0) summary += $", {failed} failed";
        return (status, summary + ".", methods);
    }

    private static void AppendJobSource(
        List<CodeFileDto> files,
        WorkloadSource source,
        string prefix)
    {
        if (source is not WorkloadSource.CodeWorkload code) return;
        foreach (var file in code.Files)
        {
            var path = prefix + "/" + file.Path.TrimStart('/');
            if (files.Any(existing => string.Equals(existing.Path, path, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Test sandbox path collision at '{path}'.");
            files.Add(new CodeFileDto(path, file.Content));
        }
    }

    private static void EnsurePytestDependency(List<CodeFileDto> files)
    {
        const string requirement = "pytest==8.4.1";
        var index = files.FindIndex(file =>
            string.Equals(file.Path, "requirements.txt", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            files.Add(new CodeFileDto("requirements.txt", requirement + "\n"));
            return;
        }

        var manifest = files[index];
        var hasPytest = manifest.Content.Split('\n')
            .Select(line => line.TrimStart())
            .Any(IsPytestRequirement);
        if (!hasPytest)
            files[index] = manifest with
            {
                Content = manifest.Content.TrimEnd() + "\n" + requirement + "\n",
            };
    }

    private static bool IsPytestRequirement(string line)
    {
        const string package = "pytest";
        if (!line.StartsWith(package, StringComparison.OrdinalIgnoreCase)) return false;
        return line.Length == package.Length
            || char.IsWhiteSpace(line[package.Length])
            || "[=<>!~".Contains(line[package.Length]);
    }

    private static JsonObject ParseScenario(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException(
                "Mock scenario is invalid: provide JSON with input and run fields.");

        JsonObject scenario;
        try
        {
            scenario = JsonNode.Parse(payload) as JsonObject
                ?? throw new InvalidOperationException("the root must be a JSON object");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Mock scenario is invalid: {ex.Message}", ex);
        }

        if (scenario["run"] is not JsonObject run)
            throw new InvalidOperationException(
                "Mock scenario is invalid: run must be a JSON object.");
        if (run["status"] is not JsonValue status
            || status.GetValueKind() != JsonValueKind.String
            || string.IsNullOrWhiteSpace(status.GetValue<string>()))
            throw new InvalidOperationException(
                "Mock scenario is invalid: run.status must be a non-empty string.");
        if (run["shards"] is null) run["shards"] = new JsonArray();
        if (run["shards"] is not JsonArray)
            throw new InvalidOperationException(
                "Mock scenario is invalid: run.shards must be an array.");
        if (!scenario.ContainsKey("input")) scenario["input"] = null;
        return scenario;
    }

    private static string ScenarioStatus(JsonObject scenario)
        => scenario["run"]!["status"]!.GetValue<string>();

    private static string? ScenarioOutput(JsonObject scenario)
    {
        var output = scenario["run"]?["output"];
        if (output is null) return null;
        return output is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : output.ToJsonString();
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string TruncateMessage(string value)
        => value.Length > 2_000 ? value[..2_000] + "…" : value;

    internal static (string Status, string Message) Evaluate(
        JobTestCaseRecord test, JobRunDetailView run, string? actual)
        => Evaluate(test, run.Status, actual);

    internal static (string Status, string Message) Evaluate(
        JobTestCaseRecord test, string runStatus, string? actual)
    {
        if (!string.Equals(runStatus, "Succeeded", StringComparison.Ordinal))
            return ("Failed", $"Expected a successful run, but the mock scenario finished as {runStatus}.");

        return test.AssertionType switch
        {
            JobTestAssertionType.Succeeds
                => ("Passed", "Job completed successfully."),
            JobTestAssertionType.OutputEquals
                => string.Equals(actual?.Trim(), test.ExpectedValue?.Trim(), StringComparison.Ordinal)
                    ? ("Passed", "Output matched exactly.")
                    : ("Failed", "Output did not match the expected value."),
            JobTestAssertionType.OutputContains
                => !string.IsNullOrEmpty(test.ExpectedValue)
                   && actual?.Contains(test.ExpectedValue, StringComparison.Ordinal) == true
                    ? ("Passed", "Output contained the expected value.")
                    : ("Failed", "Output did not contain the expected value."),
            JobTestAssertionType.JsonSubset
                => JsonSubset(test.ExpectedValue, actual, out var error)
                    ? ("Passed", "Output contained the expected JSON structure.")
                    : ("Failed", error),
            _ => ("Failed", "Unsupported assertion type."),
        };
    }

    private static bool JsonSubset(string? expected, string? actual, out string error)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            error = "Expected JSON is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(actual))
        {
            error = "The job produced no primary output.";
            return false;
        }

        try
        {
            using var expectedDoc = JsonDocument.Parse(expected);
            using var actualDoc = JsonDocument.Parse(actual);
            var matches = IsSubset(expectedDoc.RootElement, actualDoc.RootElement);
            error = matches
                ? ""
                : "Output did not contain the expected JSON structure.";
            return matches;
        }
        catch (JsonException ex)
        {
            error = $"JSON assertion could not be evaluated: {ex.Message}";
            return false;
        }
    }

    private static bool IsSubset(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        return expected.ValueKind switch
        {
            JsonValueKind.Object => expected.EnumerateObject().All(property =>
                actual.TryGetProperty(property.Name, out var actualValue)
                && IsSubset(property.Value, actualValue)),
            JsonValueKind.Array => ArraySubset(expected, actual),
            _ => JsonElement.DeepEquals(expected, actual),
        };
    }

    private static bool ArraySubset(JsonElement expected, JsonElement actual)
    {
        var expectedItems = expected.EnumerateArray().ToList();
        var actualItems = actual.EnumerateArray().ToList();
        return expectedItems.Count == actualItems.Count
               && expectedItems.Zip(actualItems).All(pair => IsSubset(pair.First, pair.Second));
    }

    private static string? Truncate(string? value)
        => value is { Length: > MaxStoredOutputLength }
            ? value[..MaxStoredOutputLength] + "\n… output truncated"
            : value;
}
