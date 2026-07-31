using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

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
            AllowNetworkEgress = command.AllowNetworkEgress,
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
    private readonly IJobRunner _runner;
    private readonly IWorkloadRunner _workloads;
    private readonly IClock _clock;
    private readonly IProjectSecretRepository? _secretRepository;
    private readonly ISecretProtector? _secretProtector;
    private readonly IOpenSearchConnectionResolver? _openSearchConnections;

    public RunJobTestCaseHandler(
        IJobTestStore tests,
        IJobRepository jobs,
        IJobRunner runner,
        IWorkloadRunner workloads,
        IClock clock,
        IProjectSecretRepository? secretRepository = null,
        ISecretProtector? secretProtector = null,
        IOpenSearchConnectionResolver? openSearchConnections = null)
    {
        (_tests, _jobs, _runner, _workloads, _clock)
            = (tests, jobs, runner, workloads, clock);
        _secretRepository = secretRepository;
        _secretProtector = secretProtector;
        _openSearchConnections = openSearchConnections;
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
            run = await _runner.RunAsync(test.JobId, test.InputPayload, ct: ct);
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
            LastJobRunId = run?.Id,
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

        var environment = await LoadEnvironmentAsync(test.ProjectId, ct);
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
                    shard.Index,
                    shard.ExitCode,
                    shard.Outcome,
                    artifact = JsonValue(shard.Artifact),
                }),
            },
        });
        var result = await _workloads.RunAsync(new WorkloadRunRequest(
            Image: null,
            StdinPayload: stdin,
            Env: environment,
            ArtifactMounts: Array.Empty<(string, string)>(),
            CorrelationId: $"job-test-{test.Id:N}-{Guid.NewGuid():N}",
            CodeFiles: test.CodeFiles.Select(file => (file.Path, file.Content)).ToList(),
            RuntimeId: test.RuntimeId,
            Entrypoint: test.Entrypoint,
            AllowNetworkEgress: test.AllowNetworkEgress,
            TimeoutSeconds: 300), ct);

        var output = FirstNonBlank(result.Artifact, result.Stdout, result.Stderr);
        return result.ExitCode == 0
            ? ("Passed", string.IsNullOrWhiteSpace(output)
                ? "Test code passed."
                : $"Test code passed: {TruncateMessage(output)}")
            : ("Failed", $"Test code exited {result.ExitCode}: {TruncateMessage(output ?? "No output.")}");
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadEnvironmentAsync(
        Guid projectId, CancellationToken ct)
    {
        var environment = new Dictionary<string, string>();
        if (_secretRepository is not null && _secretProtector is not null)
        {
            foreach (var (name, cipher) in await _secretRepository.GetCiphersAsync(projectId, ct))
            {
                try { environment[name] = _secretProtector.Unprotect(cipher); }
                catch { /* Ignore damaged unrelated Vault entries. */ }
            }
        }
        if (_openSearchConnections is not null)
            foreach (var (name, value) in await _openSearchConnections.GetJobEnvironmentAsync(projectId, ct))
                environment[name] = value;
        return environment;
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
