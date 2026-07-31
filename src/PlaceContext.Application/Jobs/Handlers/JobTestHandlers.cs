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
            existing?.AllowNetworkEgress ?? false);

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
        test.Entrypoint, test.CodeFiles, test.AllowNetworkEgress);
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
        JobRunDetailView? run = null;
        string? actual = null;
        string status;
        string message;

        try
        {
            run = await RunMockJobAsync(job, test.InputPayload, ct);
            actual = RunJobChainHandler.PrimaryOutput(run);
            (status, message) = Evaluate(test, run, actual);
            if (status == "Passed" && test.CodeFiles.Count > 0)
                (status, message) = await RunTestCodeAsync(test, run, actual, ct);
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
            UpdatedAt = _clock.UtcNow,
        };
        await _tests.SaveAsync(completed, ct);
        return SaveJobTestCaseHandler.ToView(completed, job.Name);
    }

    private async Task<(string Status, string Message)> RunTestCodeAsync(
        JobTestCaseRecord test,
        JobRunDetailView run,
        string? actual,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(test.RuntimeId))
            return ("Failed", "Test code has no runtime selected.");

        var stdin = JsonSerializer.Serialize(new
        {
            input = JsonValue(test.InputPayload),
            run = new
            {
                id = run.Id,
                status = run.Status,
                output = JsonValue(actual),
                shards = run.ShardResults.Select(shard => new
                {
                    index = shard.Index,
                    exitCode = shard.ExitCode,
                    outcome = shard.Outcome,
                    artifact = JsonValue(shard.Artifact),
                }),
            },
        });
        var result = await _workloads.RunAsync(new WorkloadRunRequest(
            Image: null,
            StdinPayload: stdin,
            Env: new Dictionary<string, string>(),
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: $"job-test-{test.Id:N}-{Guid.NewGuid():N}",
            CodeFiles: test.CodeFiles.Select(file => (file.Path, file.Content)).ToList(),
            RuntimeId: test.RuntimeId,
            Entrypoint: test.Entrypoint,
            AllowNetworkEgress: false,
            TimeoutSeconds: 300), ct);

        var output = FirstNonBlank(result.Artifact, result.Stdout, result.Stderr);
        return result.ExitCode == 0
            ? ("Passed", string.IsNullOrWhiteSpace(output)
                ? "Test code passed."
                : $"Test code passed: {TruncateMessage(output)}")
            : ("Failed", $"Test code exited {result.ExitCode}: {TruncateMessage(output ?? "No output.")}");
    }

    private async Task<JobRunDetailView> RunMockJobAsync(
        Job job, string? inputPayload, CancellationToken ct)
    {
        var startedAt = _clock.UtcNow;
        var mockRunId = Guid.NewGuid();
        var mapResult = await _workloads.RunAsync(BuildMockRequest(
            job.MapSpec.Source,
            inputPayload ?? "{}",
            Array.Empty<(string, string)>(),
            $"job-test-mock-{job.Id:N}-{mockRunId:N}-map",
            job.TimeoutSeconds), ct);
        var mapOutcome = job.ExitCodePolicy.Classify(mapResult.ExitCode);
        var shard = new ShardResultView(
            0,
            mapResult.ExitCode,
            mapOutcome.ToString(),
            mapResult.Artifact,
            CombineLog(mapResult.Stdout, mapResult.Stderr),
            ToArtifactViews(mapResult.Artifacts));

        ReduceResultView? reduce = null;
        if (mapOutcome == WorkloadOutcome.Succeeded && job.ReduceSpec is { } reduceSpec)
        {
            var mounts = mapResult.Artifact is null
                ? Array.Empty<(string, string)>()
                : new[] { (mapResult.Artifact, "/in/0/result.json") };
            var reduceResult = await _workloads.RunAsync(BuildMockRequest(
                reduceSpec.Source,
                "{}",
                mounts,
                $"job-test-mock-{job.Id:N}-{mockRunId:N}-reduce",
                job.TimeoutSeconds), ct);
            reduce = new ReduceResultView(
                reduceResult.ExitCode,
                job.ExitCodePolicy.SuccessCodes.Contains(reduceResult.ExitCode),
                reduceResult.Artifact,
                CombineLog(reduceResult.Stdout, reduceResult.Stderr),
                ToArtifactViews(reduceResult.Artifacts));
        }

        var status = mapOutcome switch
        {
            WorkloadOutcome.Failed => "Failed",
            WorkloadOutcome.Partial => "Partial",
            _ when reduce is { Succeeded: false } => "Failed",
            _ => "Succeeded",
        };
        var sourceKind = job.MapSpec.Source is WorkloadSource.CodeWorkload ? "code" : "image";
        return new JobRunDetailView(
            mockRunId,
            job.Id,
            job.ProjectId,
            status,
            startedAt,
            _clock.UtcNow,
            new[] { shard },
            reduce,
            new JobRunSnapshotView(
                sourceKind,
                job.MapSpec.Source.Label,
                job.ReduceSpec is null
                    ? null
                    : job.ReduceSpec.Source is WorkloadSource.CodeWorkload ? "code" : "image",
                job.ReduceSpec?.Source.Label,
                1,
                1,
                false));
    }

    private static WorkloadRunRequest BuildMockRequest(
        WorkloadSource source,
        string stdinPayload,
        IReadOnlyList<(string Content, string ContainerPath)> artifactMounts,
        string correlationId,
        int timeoutSeconds)
        => source switch
        {
            WorkloadSource.ImageWorkload image => new WorkloadRunRequest(
                image.Image, stdinPayload, new Dictionary<string, string>(), artifactMounts,
                correlationId, null, null, null, false, timeoutSeconds),
            WorkloadSource.CodeWorkload code => new WorkloadRunRequest(
                null, stdinPayload, new Dictionary<string, string>(), artifactMounts,
                correlationId,
                code.Files.Select(file => (file.Path, file.Content)).ToList(),
                code.RuntimeId, code.Entrypoint, false, timeoutSeconds),
            _ => throw new InvalidOperationException(
                $"Unsupported workload source type {source.GetType().Name}."),
        };

    private static IReadOnlyList<RunArtifactView> ToArtifactViews(
        IReadOnlyList<WorkloadArtifact>? artifacts)
        => artifacts is null
            ? Array.Empty<RunArtifactView>()
            : artifacts.Select(artifact =>
                new RunArtifactView(artifact.Name, artifact.Content, artifact.IsBinary)).ToList();

    private static string? CombineLog(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr)) return null;
        if (string.IsNullOrWhiteSpace(stderr)) return stdout;
        if (string.IsNullOrWhiteSpace(stdout)) return stderr;
        return stdout + "\n--- stderr ---\n" + stderr;
    }

    private static JsonNode? JsonValue(string? value)
    {
        if (value is null) return null;
        try { return JsonNode.Parse(value); }
        catch (JsonException) { return System.Text.Json.Nodes.JsonValue.Create(value); }
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string TruncateMessage(string value)
        => value.Length > 2_000 ? value[..2_000] + "…" : value;

    internal static (string Status, string Message) Evaluate(
        JobTestCaseRecord test, JobRunDetailView run, string? actual)
    {
        if (!string.Equals(run.Status, "Succeeded", StringComparison.Ordinal))
            return ("Failed", $"Expected a successful run, but the job finished as {run.Status}.");

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
