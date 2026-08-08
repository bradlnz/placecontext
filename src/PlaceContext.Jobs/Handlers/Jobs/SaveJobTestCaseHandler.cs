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
