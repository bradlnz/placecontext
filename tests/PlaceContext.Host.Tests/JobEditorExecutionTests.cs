using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload;
using PlaceContext.Application;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class JobEditorExecutionTests
{
    [Theory]
    [InlineData(0, "Succeeded")]
    [InlineData(1, "Failed")]
    public async Task Run_saves_current_code_and_shows_live_then_completed_container_results(int exitCode, string status)
    {
        await using var harness = new Harness();
        var vm = harness.Vm;
        await vm.AfterRenderAsync();
        await vm.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var request = await harness.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.PanelOpen);
        Assert.True(vm.Running);
        Assert.Equal("python", request.RuntimeId);
        Assert.Equal("print('edited')", Assert.Single(request.CodeFiles!).Content);
        Assert.Equal("{\"value\":42}", request.StdinPayload);
        await UntilAsync(() => vm.LastRun?.Status == "Running");
        await vm.RunAsync();
        Assert.Equal(1, harness.Launches);

        harness.Completed.SetResult(new(exitCode, "{\"ok\":true}", "container output", ""));
        await UntilAsync(() => !vm.Running);
        Assert.Equal(status, vm.LastRun?.Status);
        Assert.Equal("container output", Assert.Single(vm.LastRun!.ShardResults).Log);
        Assert.Null(vm.ExecutionError);
    }

    [Fact]
    public async Task Container_start_failure_is_visible_in_execution_results()
    {
        await using var harness = new Harness();
        await harness.Vm.RunAsync();
        await harness.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Completed.SetException(new InvalidOperationException("Container runtime unavailable"));
        await UntilAsync(() => !harness.Vm.Running);
        Assert.True(harness.Vm.PanelOpen);
        Assert.Equal("Container runtime unavailable", harness.Vm.ExecutionError);
    }

    [Fact]
    public async Task Results_follow_the_final_retry_instead_of_stopping_at_the_first_failure()
    {
        await using var harness = new Harness();
        harness.FinalAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await harness.Vm.RunAsync();
        await harness.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Completed.SetResult(new(1, null, "first attempt failed", ""));
        await UntilAsync(() => harness.Vm.LastRun?.Status == "Failed");
        Assert.True(harness.Vm.Running);

        var final = harness.Vm.LastRun! with
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            AttemptNumber = 2
        };
        harness.FinalAttempt.SetResult(final);
        await UntilAsync(() => !harness.Vm.Running);
        Assert.Equal(final, harness.Vm.LastRun);
    }

    [Fact]
    public async Task Leaving_editor_stops_updates_but_does_not_cancel_container_execution()
    {
        await using var harness = new Harness();
        await harness.Vm.RunAsync();
        await harness.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Vm.DisposeAsync();
        var lastRun = harness.Vm.LastRun;
        harness.Completed.SetResult(new(0, "{}", "done", ""));
        await UntilAsync(() => harness.Operations.ListForTenant(Harness.Tenant.Id)
            .Any(op => op.Status == PortalOperationStatus.Succeeded));
        Assert.Same(lastRun, harness.Vm.LastRun);
    }

    [Fact]
    public async Task Returning_to_the_same_job_reinitializes_its_editor()
    {
        await using var harness = new Harness();
        await harness.Vm.AfterRenderAsync();
        await harness.Vm.DisposeAsync();
        harness.Vm.Initialize(harness.Vm.ProjectId, harness.Vm.JobId);
        await harness.Vm.AfterRenderAsync();
        Assert.Equal(2, harness.EditorInitializations);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class Harness : IDispatcher, IJobRunner, IWorkloadRunner, IJSRuntime, IHostApplicationLifetime, IAsyncDisposable
    {
        public static readonly TenantInfo Tenant = new(Guid.NewGuid(), "editor", "Editor", "UTC");
        private readonly InMemoryJobRepository _jobs = new();
        private readonly InMemoryJobRunRepository _runs = new();
        private readonly RecordingUnitOfWork _uow = new();
        private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
        private readonly ServiceProvider _provider;
        public JobEditorViewModel Vm
        {
            get;
        }
        public OperationCenter Operations
        {
            get;
        }
        public int Launches
        {
            get; private set;
        }
        public int EditorInitializations
        {
            get; private set;
        }
        public TaskCompletionSource<WorkloadRunRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WorkloadRunResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JobRunDetailView>? FinalAttempt
        {
            get; set;
        }

        public Harness()
        {
            CurrentTenant.Set(Tenant);
            var job = Job.Create(Guid.NewGuid(), "Editor job", null,
                new MapSpec(new WorkloadSource.CodeWorkload("python", "print('old')", "main.py"),
                    ["{\"value\":42}"], new Dictionary<string, string>()), null, 1, new ExitCodePolicy([0], []), _clock.UtcNow);
            _jobs.AddAsync(job).GetAwaiter().GetResult();
            var service = new PlaceContextService(this, this, null!, null!);
            _provider = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
            Operations = new(_provider.GetRequiredService<IServiceScopeFactory>(), ApplicationStopping);
            Vm = new(service, new PortalUiState(), null!, this, Operations);
            Vm.Initialize(job.ProjectId, job.Id);
        }

        public async Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => command switch
        {
            UploadJobCodeCommand upload => (T)(object)await new UploadJobCodeHandler(_jobs, _uow, _clock).HandleAsync(upload, ct),
            _ => throw new NotSupportedException(),
        };

        public async Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => query switch
        {
            GetJobQuery job => (T)(object)(await new GetJobHandler(_jobs).HandleAsync(job, ct))!,
            GetJobRunQuery run => (T)(object)(await new GetJobRunHandler(_runs).HandleAsync(run, ct))!,
            _ => throw new NotSupportedException(),
        };

        public async Task<JobRunDetailView> RunAsync(Guid jobId, string? inputPayload = null, Guid? runId = null,
            Guid? replayOfRunId = null, CancellationToken ct = default)
        {
            var run = await new RunJobHandler(_jobs, _runs, this, _uow, _clock)
                .HandleAsync(new RunJobCommand(jobId, inputPayload, runId), ct);
            return FinalAttempt is null ? run : await FinalAttempt.Task.WaitAsync(ct);
        }

        public Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
        {
            Launches++;
            Started.TrySetResult(request);
            return Completed.Task.WaitAsync(ct);
        }

        public Task CancelAsync(string correlationId, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "pcmonaco.init")
            {
                EditorInitializations++;
            }
            return ValueTask.FromResult(identifier == "pcmonaco.init" ? (T)(object)true :
                            identifier == "pcmonaco.getValue" ? (T)(object)"print('edited')" : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<T>(identifier, args);
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
        }
        public async ValueTask DisposeAsync()
        {
            await Vm.DisposeAsync();
            await _provider.DisposeAsync();
            CurrentTenant.Clear();
        }
    }
}
