using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class UploadJobCodeHandler : ICommandHandler<UploadJobCodeCommand, JobView>
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UploadJobCodeHandler(IJobRepository jobs, IUnitOfWork uow, IClock clock)
    {
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<JobView> HandleAsync(UploadJobCodeCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.RuntimeId))
            throw new ArgumentException("RuntimeId is required.", nameof(command));
        if (command.Files is null || command.Files.Count == 0)
            throw new ArgumentException("At least one file is required.", nameof(command));

        var files = command.Files.Select(f => new CodeFile(f.Path, f.Content)).ToList();
        var codeSource = new WorkloadSource.CodeWorkload(command.RuntimeId, files, command.Entrypoint);

        var job = await ResolveTargetAsync(command, ct);

        if (job is null)
        {
            // Create a new code job with sensible defaults.
            if (command.ProjectId is not { } projectId || string.IsNullOrWhiteSpace(command.JobName))
                throw new ArgumentException("ProjectId and JobName are required to create a new job.", nameof(command));

            var mapSpec = new MapSpec(codeSource, new[] { "{}" }, new Dictionary<string, string>());
            var policy = new ExitCodePolicy(new[] { 0 }, Array.Empty<int>());
            // Jobs exist to generate artifacts: editor-created jobs declare a Chart return type, so
            // every run yields a chart artifact (the run-history panel and Reports surface it).
            job = Job.Create(projectId, command.JobName!, null, mapSpec, reduceSpec: null,
                concurrencyLimit: 1, exitCodePolicy: policy, createdAt: _clock.UtcNow,
                returnType: JobReturnType.Chart);
            await _jobs.AddAsync(job, ct);
        }
        else
        {
            // Replace the map source, preserving everything else about the job.
            var mapSpec = new MapSpec(codeSource, job.MapSpec.InputPayloads, job.MapSpec.Env);
            job.Update(job.Name, job.Description, mapSpec, job.ReduceSpec, job.ConcurrencyLimit,
                job.ExitCodePolicy, _clock.UtcNow, job.AllowNetworkEgress,
                allowApiInvocation: job.AllowApiInvocation,
                timeoutSeconds: job.TimeoutSeconds, returnType: job.ReturnType, returnFileName: job.ReturnFileName,
                retryCount: job.RetryCount, retryDelaySeconds: job.RetryDelaySeconds);
            await _jobs.UpdateAsync(job, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return JobViewMapper.ToView(job);
    }

    private async Task<Job?> ResolveTargetAsync(UploadJobCodeCommand command, CancellationToken ct)
    {
        if (command.JobId is { } id)
            return await _jobs.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Job {id} not found.");

        if (command.ProjectId is { } projectId && !string.IsNullOrWhiteSpace(command.JobName))
        {
            var existing = await _jobs.ListForProjectAsync(projectId, ct);
            return existing.FirstOrDefault(j =>
                string.Equals(j.Name, command.JobName, StringComparison.OrdinalIgnoreCase));
        }

        throw new ArgumentException("Provide either JobId, or ProjectId + JobName.", nameof(command));
    }
}
