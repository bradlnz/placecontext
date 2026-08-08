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
        var code = (Domain.ValueObjects.CodeWorkload)source;
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
