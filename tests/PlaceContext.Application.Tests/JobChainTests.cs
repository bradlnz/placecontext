using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Communications;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using PlaceContext.Application.Access;

namespace PlaceContext.Application.Tests;

public class JobChainTests
{
    [Fact]
    public async Task Safety_rejection_stops_job_and_parent_retries_and_remains_in_run_history()
    {
        var job = MakeJob("blocked"); var jobs = new InMemoryJobRepository(); await jobs.AddAsync(job);
        var child = JobChain.Create(ProjectId, "Child", null, [new ChainStage([job.Id], executionPolicy: new(3, 0))], T0);
        var parent = JobChain.Create(ProjectId, "Parent", null,
            [new ChainStage([], action: new SubWorkflowChainAction(child.Id, child.Name), executionPolicy: new(3, 0))], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(child); await chains.AddAsync(parent);
        var runs = new InMemoryChainRunRepository(); var dispatcher = new FakeRunDispatcher();
        dispatcher.Failures[job.Id] = new ExecutionSafetyException("AI safety review blocked execution.");
        var result = await RunHandler(chains, jobs, dispatcher, runs, permissions: new FakePermissionService(true)).HandleAsync(new(parent.Id));
        Assert.Equal("Failed", result.Status);
        Assert.True(ExecutionSafetyException.IsBlocked(result));
        Assert.Equal(0, result.Steps[0].RetryCount);
        Assert.Single(dispatcher.DispatchedJobIds);
        var saved = Assert.Single(await runs.ListForChainAsync(child.Id, 10));
        Assert.Contains(saved.Steps[0].AgentActivity!, activity => activity.Kind == ExecutionSafetyException.ActivityKind);
    }

    [Fact]
    public async Task Parent_step_timeout_cancels_child_run_before_retrying()
    {
        var job = MakeJob("slow child"); var jobs = new InMemoryJobRepository(); await jobs.AddAsync(job);
        var child = JobChain.Create(ProjectId, "Child", null, [ChainStage.Of(job.Id)], T0);
        var parent = JobChain.Create(ProjectId, "Parent", null,
            [new ChainStage([], action: new SubWorkflowChainAction(child.Id, child.Name), executionPolicy: new(1, 0, 1))], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(child); await chains.AddAsync(parent);
        var runs = new InMemoryChainRunRepository(); var dispatcher = new FakeRunDispatcher();
        dispatcher.Delays[job.Id] = TimeSpan.FromSeconds(30);
        var result = await RunHandler(chains, jobs, dispatcher, runs, permissions: new FakePermissionService(true)).HandleAsync(new(parent.Id));
        Assert.Equal("Failed", result.Status); Assert.Contains("execution limit", result.Steps[0].Error);
        Assert.Equal(1, result.Steps[0].RetryCount);
        var children = await runs.ListForChainAsync(child.Id, 10); Assert.Equal(2, children.Count);
        Assert.All(children, childRun => { Assert.Equal(ChainRunStatus.Cancelled, childRun.Status); Assert.All(childRun.Steps, step => Assert.Equal(ChainStepStatus.Cancelled, step.Status)); });
        Assert.Equal(1, dispatcher.MaxObservedConcurrency);
    }

    [Fact]
    public async Task Parallel_workflow_retry_does_not_repeat_successful_siblings()
    {
        var chains = new InMemoryJobChainRepository();
        var children = Enumerable.Range(0, 2).Select(i => JobChain.Create(ProjectId, $"Child {i}", null, [ChainStage.Of(Guid.NewGuid())], T0)).ToArray();
        foreach (var child in children) await chains.AddAsync(child);
        var parent = JobChain.Create(ProjectId, "Parent", null,
            [new ChainStage([], action: new ParallelWorkflowsChainAction(children.Select(child => new ConditionalWorkflowBranch(child.Id, child.Name, "true")).ToArray()), executionPolicy: new(2, 0, 10))], T0);
        await chains.AddAsync(parent);
        var dispatcher = new ParallelChildDispatcher(children[0].Id, true); dispatcher.Release.SetResult();
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), new InMemoryChainRunRepository(), new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(new FakeRunDispatcher()), permissions: new FakePermissionService(true), dispatcher: dispatcher);
        var result = await handler.HandleAsync(new(parent.Id, "{\"approved\":true}"));
        Assert.Equal("Failed", result.Status); Assert.Equal(4, dispatcher.Started); Assert.Equal(2, result.Steps[0].RetryCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Step_policy_retries_failed_jobs_individually_including_false_paths(bool inElse, bool parallel)
    {
        var jobs = new InMemoryJobRepository(); var job = MakeJob("retry"); var other = MakeJob("other");
        await jobs.AddAsync(job); await jobs.AddAsync(other);
        var failing = new ChainStage(parallel ? [job.Id, other.Id] : [job.Id], executionPolicy: new(2, 0, 10));
        var chain = JobChain.Create(ProjectId, "bounded", null,
            [inElse ? new ChainStage([other.Id], new ConditionGate("false", [failing])) : failing], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository(); var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[job.Id] = Run(job.Id, "Failed"); dispatcher.Results[other.Id] = Run(other.Id, "Succeeded");
        var result = await RunHandler(chains, jobs, dispatcher, runs).HandleAsync(new(chain.Id));
        Assert.Equal("Failed", result.Status);
        Assert.Equal(3, dispatcher.DispatchedJobIds.Count(id => id == job.Id));
        Assert.Equal(parallel ? 1 : 0, dispatcher.DispatchedJobIds.Count(id => id == other.Id));
        Assert.Equal(dispatcher.RunIds.Count, dispatcher.RunIds.Distinct().Count());
        Assert.Equal(2, Assert.Single(result.Steps, step => step.JobId == job.Id).RetryCount);
    }

    [Fact]
    public async Task Step_timeout_fails_the_step_and_skips_following_jobs()
    {
        var jobs = new InMemoryJobRepository(); var job = MakeJob("slow"); var next = MakeJob("next");
        await jobs.AddAsync(job); await jobs.AddAsync(next);
        var chain = JobChain.Create(ProjectId, "timeout", null,
            [new ChainStage([job.Id], executionPolicy: new(0, 0, 1)), ChainStage.Of(next.Id)], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher(); dispatcher.Delays[job.Id] = TimeSpan.FromSeconds(30);
        var result = await RunHandler(chains, jobs, dispatcher, new InMemoryChainRunRepository()).HandleAsync(new(chain.Id));
        Assert.Equal("Failed", result.Status); Assert.Contains("execution limit", result.Steps[0].Error);
        Assert.Equal("Skipped", result.Steps[1].Status); Assert.Single(dispatcher.DispatchedJobIds);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 6)]
    public async Task Parallel_conditions_start_matching_children_together_and_join_in_branch_order(bool fail, int branchCount)
    {
        var chains = new InMemoryJobChainRepository();
        var children = Enumerable.Range(0, branchCount).Select(i => JobChain.Create(ProjectId, $"Child {i}", null, [ChainStage.Of(Guid.NewGuid())], T0)).ToArray();
        foreach (var child in children) await chains.AddAsync(child);
        var action = new ParallelWorkflowsChainAction(children.Select((child, i) => new ConditionalWorkflowBranch(child.Id, child.Name,
            i == branchCount - 1 ? "eq:approved:false" : "all:[\"eq:approved:true\",\"gte:score:0.9\"]")).ToArray());
        var next = MakeJob("join"); var jobs = new InMemoryJobRepository(); await jobs.AddAsync(next);
        var parent = JobChain.Create(ProjectId, "Parent", null, [ChainStage.ForAction(action), ChainStage.Of(next.Id)], T0);
        await chains.AddAsync(parent);
        var jobDispatcher = new FakeRunDispatcher(); jobDispatcher.Results[next.Id] = Run(next.Id, "Succeeded");
        var dispatcher = new ParallelChildDispatcher(children[0].Id, fail);
        var runs = new InMemoryChainRunRepository();
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(jobDispatcher), permissions: new FakePermissionService(true), dispatcher: dispatcher);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = handler.HandleAsync(new(parent.Id, "{\"approved\":true,\"score\":0.95}"), timeout.Token);
        await dispatcher.BothStarted.Task.WaitAsync(timeout.Token);
        Assert.False(running.IsCompleted); Assert.Equal(Math.Min(branchCount - 1, 4), dispatcher.Started);
        var progress = Assert.Single(await runs.ListForChainAsync(parent.Id, 1));
        using var live = System.Text.Json.JsonDocument.Parse(progress.Steps[0].Output!);
        Assert.Equal("Running", live.RootElement[0].GetProperty("status").GetString());
        Assert.Equal("Skipped", live.RootElement[branchCount - 1].GetProperty("status").GetString());
        if (branchCount == 6) Assert.Equal("Queued", live.RootElement[4].GetProperty("status").GetString());
        dispatcher.Release.TrySetResult();
        var result = await running;
        Assert.Equal(fail ? "Failed" : "Succeeded", result.Status);
        Assert.Equal(fail ? "Skipped" : "Succeeded", result.Steps[1].Status);
        using var output = System.Text.Json.JsonDocument.Parse(result.Steps[0].Output!);
        Assert.Equal(children[0].Id, output.RootElement[0].GetProperty("workflowId").GetGuid());
        Assert.Equal(children[1].Id, output.RootElement[1].GetProperty("workflowId").GetGuid());
        Assert.Equal("Skipped", output.RootElement[branchCount - 1].GetProperty("status").GetString());
        Assert.Contains("parallel_results", result.FinalOutput);
    }

    private sealed class ParallelChildDispatcher(Guid first, bool fail) : IDispatcher
    {
        public int Started;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            var run = Assert.IsType<RunJobChainCommand>(command);
            Assert.Contains("approved", run.InputPayload);
            if (Interlocked.Increment(ref Started) == 2) BothStarted.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return (T)(object)new ChainRunView(Guid.NewGuid(), run.ChainId, "Child", fail && run.ChainId == first ? "Failed" : "Succeeded", [],
                System.Text.Json.JsonSerializer.Serialize(new { child = run.ChainId }), T0, T0.AddSeconds(1));
        }
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("Succeeded", false)]
    [InlineData("Failed", false)]
    [InlineData("Partial", false)]
    [InlineData("Succeeded", true)]
    public async Task Sub_workflows_pass_payloads_return_results_and_propagate_failure(string outcome, bool inElse)
    {
        var jobs = new InMemoryJobRepository(); var job = MakeJob("specialist"); var next = MakeJob("next");
        await jobs.AddAsync(job); await jobs.AddAsync(next);
        var chains = new InMemoryJobChainRepository();
        var child = JobChain.Create(ProjectId, "Specialist", null, [ChainStage.Of(job.Id)], T0);
        await chains.AddAsync(child);
        var call = ChainStage.ForAction(new SubWorkflowChainAction(child.Id, child.Name));
        var parent = JobChain.Create(ProjectId, "Parent", null,
            [inElse ? new ChainStage([next.Id], new ConditionGate("false", [call])) : call, ChainStage.Of(next.Id)], T0);
        await chains.AddAsync(parent);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[job.Id] = Run(job.Id, outcome, ["{\"reviewed\":true}"]);
        dispatcher.Results[next.Id] = Run(next.Id, "Succeeded");
        var runs = new InMemoryChainRunRepository();
        var admission = new SingleWorkflowAdmission();
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(dispatcher), permissions: new FakePermissionService(true), allowance: admission, capacity: admission);
        var result = await handler.HandleAsync(new(parent.Id, "{\"question\":\"Hello\"}"));
        Assert.Equal(outcome, result.Status);
        Assert.Equal(1, admission.Reservations);
        Assert.Equal(1, admission.Leases);
        Assert.True(admission.Disposed);
        Assert.Equal(outcome == "Failed" ? "Skipped" : "Succeeded", result.Steps[1].Status);
        var step = Assert.Single(result.Steps, step => step.ActionType == SubWorkflowChainAction.ActionType);
        using var output = System.Text.Json.JsonDocument.Parse(step.Output!);
        var childRun = (await runs.GetByIdAsync(output.RootElement.GetProperty("runId").GetGuid()))!;
        Assert.Equal(child.Id, childRun.ChainId);
        Assert.Contains("Hello", childRun.FinalOutput);
        if (outcome != "Failed") Assert.Contains("reviewed", result.FinalOutput);
    }

    private sealed class SingleWorkflowAdmission : IRunAllowance, IExecutionCapacity, IExecutionLease
    {
        public int Reservations; public int Leases; public bool Disposed;
        public CancellationToken LostToken => CancellationToken.None;
        public Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default)
        { Reservations++; return Task.CompletedTask; }
        public Task<IExecutionLease> AcquireAsync(Guid executionId, string kind, CancellationToken ct = default)
        { Assert.Equal(0, Leases++); return Task.FromResult<IExecutionLease>(this); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Cancelling_parent_stops_the_active_specialist()
    {
        var chains = new InMemoryJobChainRepository();
        var child = JobChain.Create(ProjectId, "Specialist", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Work", "result", agentProvider: LoopChainAction.AutomaticAgent, connectionId: Guid.NewGuid(), model: "test"))], T0);
        var parent = JobChain.Create(ProjectId, "Parent", null, [ChainStage.ForAction(new SubWorkflowChainAction(child.Id, child.Name))], T0);
        await chains.AddAsync(child); await chains.AddAsync(parent);
        var runs = new InMemoryChainRunRepository(); var agent = new ProgressWorkflowAgent();
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), runs, new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(new FakeRunDispatcher()), agentRunner: agent, permissions: new FakePermissionService(true));
        var id = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var task = handler.HandleAsync(new(parent.Id, ChainRunId: id), timeout.Token);
        ChainRun? saved;
        do { await Task.Delay(20, timeout.Token); saved = await runs.GetByIdAsync(id); }
        while (saved?.Steps[0].Status != ChainStepStatus.Running);
        saved.Complete(ChainRunStatus.Cancelled, saved.FinalOutput, T0);
        agent.Release.TrySetResult();
        Assert.Equal("Cancelled", (await task).Status);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("wait")]
    public async Task Sub_workflows_reject_unsafe_references_before_execution(string kind)
    {
        var chains = new InMemoryJobChainRepository();
        var child = JobChain.Create(kind == "foreign" ? Guid.NewGuid() : ProjectId, "Child", null,
            [new ChainStage([Guid.NewGuid()], kind == "wait" ? new WaitGate(TimeSpan.FromSeconds(1)) : null)], T0);
        await chains.AddAsync(child);
        var parent = JobChain.Create(ProjectId, "Parent", null,
            [ChainStage.ForAction(new SubWorkflowChainAction(kind == "missing" ? Guid.NewGuid() : child.Id, "Child"))], T0);
        if (kind == "cycle") child.Update("Child", null, [ChainStage.ForAction(new SubWorkflowChainAction(parent.Id, "Parent"))], T0);
        await chains.AddAsync(parent);
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), new InMemoryChainRunRepository(),
            new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(new FakeRunDispatcher()), permissions: new FakePermissionService(true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new(parent.Id)));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 9)]
    public async Task Container_steps_pass_outputs_to_Qemu_and_always_release_the_sandbox(bool allowed, int exitCode)
    {
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "build and test", null,
            [ChainStage.ForAction(new ContainerBuildChainAction("FROM alpine:3.21")),
             ChainStage.ForAction(new ContainerRunChainAction(port:8080)),
             ChainStage.ForAction(new VirtualMachineChainAction("browser", "Test container_run.qemuUrl", allowNetwork:true))], T0);
        await chains.AddAsync(chain);
        var containers = new FakeContainers(exitCode);
        var browser = new ContainerBrowser();
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), new InMemoryChainRunRepository(),
            new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(new FakeRunDispatcher()),
            permissions: new FakePermissionService(allowed), qemu: browser, containers: containers);
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id, "{\"original\":42}"));
        Assert.True(containers.Disposed);
        Assert.Equal(allowed && exitCode == 0 ? "Succeeded" : "Failed", result.Status);
        Assert.Equal(allowed && exitCode == 0, browser.Called);
        Assert.Equal(!allowed ? 0 : exitCode == 0 ? 2 : 1, containers.Calls);
        if (allowed)
        {
            Assert.Contains("\"exitCode\":" + exitCode, result.Steps[0].Output);
        }
    }

    private sealed class FakeContainers(int exitCode) : IContainerWorkflowRunner, IAsyncDisposable
    {
        public int Calls; public bool Disposed;
        public IAsyncDisposable TrackRun(Guid runId) => this;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
        public Task<ContainerStepResult> ExecuteAsync(ChainAction action, Guid projectId, Guid runId, int step, string? payload, CancellationToken ct)
        {
            Calls++;
            if (action is ContainerRunChainAction)
            {
                Assert.Contains("\"image\":\"built-image\"", payload);
            }
            return Task.FromResult(new ContainerStepResult(exitCode, "container output", exitCode == 0 ? "" : "failed", "built-image", QemuUrl: "http://10.0.2.2:1234"));
        }
    }
    private sealed class ContainerBrowser : IVirtualMachineEnvironmentRunner
    {
        public bool Called;
        public Task<VirtualMachineEnvironmentResult> RunAsync(VirtualMachineChainAction action, string? input, CancellationToken ct,
            Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineRunContext? context = null)
        {
            Called = true; Assert.Contains("http://10.0.2.2:1234", input); Assert.Contains("\"original\":42", input);
            return Task.FromResult(new VirtualMachineEnvironmentResult(0, "Tested", ""));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_prevents_waiting_fanout_branches_from_starting(bool inElseBranch)
    {
        var jobs = new InMemoryJobRepository();
        var group = Enumerable.Range(0, 6).Select(i => MakeJob($"parallel-{i}")).ToArray();
        var next = MakeJob("must-not-run");
        foreach (var job in group.Append(next))
        {
            await jobs.AddAsync(job);
        }
        var fanout = new ChainStage(group.Select(job => job.Id).ToArray());
        var first = inElseBranch
            ? new ChainStage(new[] { next.Id }, new ConditionGate("false", new[] { fanout }))
            : fanout;
        var chain = JobChain.Create(ProjectId, "cancel fanout", null, new[] { first, ChainStage.Of(next.Id) }, T0);
        var chains = new InMemoryJobChainRepository();
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var runner = new BlockedCancellationRunner();
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0), runner);
        var runId = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var executing = handler.HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: runId), timeout.Token);
        await runner.FourStarted.Task.WaitAsync(timeout.Token);
        var stored = (await runs.GetByIdAsync(runId))!;
        stored.Complete(ChainRunStatus.Cancelled, stored.FinalOutput, T0);
        runner.Release.TrySetResult();
        var result = await executing;
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(4, runner.Started);
    }

    private sealed class BlockedCancellationRunner : IJobRunner
    {
        public TaskCompletionSource FourStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;

        public async Task<JobRunDetailView> RunAsync(Guid jobId, string? inputPayload = null,
            Guid? runId = null, Guid? replayOfRunId = null, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref Started) == 4)
            {
                FourStarted.TrySetResult();
            }
            await Release.Task.WaitAsync(ct);
            return Run(jobId, "Cancelled");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runner_exception_finishes_workflow_and_failed_step_including_else_branch(bool inElseBranch)
    {
        var jobs = new InMemoryJobRepository();
        var failing = MakeJob("failing");
        var sibling = MakeJob("sibling");
        var next = MakeJob("must-not-run");
        foreach (var job in new[] { failing, sibling, next }) await jobs.AddAsync(job);
        var failingStage = new ChainStage(new[] { failing.Id, sibling.Id });
        var first = inElseBranch
            ? new ChainStage(new[] { next.Id }, new ConditionGate("false", new[] { failingStage }))
            : failingStage;
        var chain = JobChain.Create(ProjectId, "runner failure", null, new[] { first, ChainStage.Of(next.Id) }, T0);
        var chains = new InMemoryJobChainRepository();
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Failures[failing.Id] = new HttpRequestException("Workload server unavailable");
        dispatcher.Results[sibling.Id] = Run(sibling.Id, "Succeeded");
        dispatcher.Delays[sibling.Id] = TimeSpan.FromMilliseconds(20);
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(),
            new FakeClock(T0), new FakeJobRunner(dispatcher));
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id));
        var saved = (await runs.GetByIdAsync(result.Id))!;
        Assert.Equal(ChainRunStatus.Failed, saved.Status);
        Assert.NotNull(saved.FinishedAt);
        Assert.DoesNotContain(saved.Steps, step => step.Status is ChainStepStatus.Running or ChainStepStatus.Pending);
        var failed = Assert.Single(saved.Steps, step => step.JobId == failing.Id);
        Assert.Equal(ChainStepStatus.Failed, failed.Status);
        Assert.NotNull(failed.RunId);
        Assert.Contains("Workload server unavailable", failed.Error);
        Assert.Equal(ChainStepStatus.Succeeded, Assert.Single(saved.Steps, step => step.JobId == sibling.Id).Status);
        Assert.DoesNotContain(next.Id, dispatcher.DispatchedJobIds);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(0, false)]
    public async Task Qemu_results_flow_forward_and_failure_or_denied_permission_stops_jobs(int exitCode, bool allowed)
    {
        var jobs = new InMemoryJobRepository();
        var next = MakeJob("after-vm");
        await jobs.AddAsync(next);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "vm", null,
            [ChainStage.ForAction(new VirtualMachineChainAction("linux", "cat")), ChainStage.Of(next.Id)], T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[next.Id] = Run(next.Id, "Succeeded");
        var qemu = new FakeVirtualMachineRunner(exitCode);
        var replay = new FakeVirtualMachineReplayStore();
        var handler = new RunJobChainHandler(chains, jobs, new InMemoryChainRunRepository(), new RecordingUnitOfWork(),
            new FakeClock(T0), new FakeJobRunner(dispatcher), permissions: new FakePermissionService(allowed), qemu: qemu, qemuReplay: replay);
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id, "{\"order\":42}"));
        Assert.Equal(allowed && exitCode == 0 ? "Succeeded" : "Failed", result.Status);
        Assert.Equal(allowed, qemu.Called);
        Assert.Equal(allowed ? 1 : 0, replay.Captures.Count);
        Assert.Equal(allowed && exitCode == 0 ? 1 : 0, dispatcher.DispatchedJobIds.Count);
        if (allowed)
        {
            using var json = System.Text.Json.JsonDocument.Parse(result.FinalOutput!);
            Assert.Equal(42, json.RootElement.GetProperty("order").GetInt32());
            Assert.Equal(exitCode, json.RootElement.GetProperty("qemu_result").GetProperty("exitCode").GetInt32());
            Assert.Equal("guest output", json.RootElement.GetProperty("qemu_result").GetProperty("stdout").GetString());
            Assert.Equal(3, json.RootElement.GetProperty("qemu_result").GetProperty("agentSteps").GetInt32());
            Assert.Equal("guest_shell", json.RootElement.GetProperty("qemu_result").GetProperty("activity")[0].GetProperty("Tool").GetString());
            Assert.Equal(result.Id, replay.Captures[0].RunId);
            Assert.Equal(0, replay.Captures[0].Step);
            using var stepOutput = System.Text.Json.JsonDocument.Parse(result.Steps[0].Output!);
            Assert.Equal("guest output", stepOutput.RootElement.GetProperty("stdout").GetString());
            Assert.False(stepOutput.RootElement.TryGetProperty("order", out _));
            Assert.DoesNotContain("c2NyZWVu", result.FinalOutput!);
        }
    }

    private sealed class FakeVirtualMachineRunner(int exitCode) : IVirtualMachineEnvironmentRunner
    {
        public bool Called;
        public async Task<VirtualMachineEnvironmentResult> RunAsync(VirtualMachineChainAction action, string? input, CancellationToken ct,
            Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineRunContext? context = null)
        {
            Called = true;
            Assert.Equal("{\"order\":42}", input);
            if (recordFrame is not null)
            {
                await recordFrame(new(new(0, 0, T0, "Desktop ready", null, true), "screen"u8.ToArray()), ct);
            }
            return new VirtualMachineEnvironmentResult(exitCode, "guest output", "", AgentSteps: 3,
                            Activity: [new("guest_shell", "Opened the browser", 0)]);
        }
    }

    [Fact]
    public async Task Cancelling_a_workflow_stops_the_guest_and_does_not_run_the_next_step()
    {
        var chains = new InMemoryJobChainRepository();
        var jobs = new InMemoryJobRepository();
        var next = MakeJob("after-vm");
        await jobs.AddAsync(next);
        var chain = JobChain.Create(ProjectId, "cancel-vm", null,
            [ChainStage.ForAction(new VirtualMachineChainAction("linux", "sleep 120")), ChainStage.Of(next.Id)], T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        var guest = new WaitingVirtualMachineRunner();
        var replay = new FakeVirtualMachineReplayStore();
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(dispatcher), permissions: new FakePermissionService(true), qemu: guest, qemuReplay: replay);
        var runId = Guid.NewGuid();
        var task = handler.HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: runId));
        await guest.Started.Task;
        (await runs.GetByIdAsync(runId))!.Complete(ChainRunStatus.Cancelled, null, T0);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", result.Status);
        Assert.True(guest.Stopped);
        Assert.Equal(runId, Assert.Single(replay.Captures).RunId);
        Assert.Empty(dispatcher.DispatchedJobIds);
    }

    private sealed class WaitingVirtualMachineRunner : IVirtualMachineEnvironmentRunner
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped;
        public async Task<VirtualMachineEnvironmentResult> RunAsync(VirtualMachineChainAction action, string? input, CancellationToken ct,
            Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineRunContext? context = null)
        {
            if (recordFrame is not null)
            {
                await recordFrame(new(new(0, 0, T0, "Desktop ready", null, true), "screen"u8.ToArray()), ct);
            }
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                Stopped = true;
            }
            return new(0, "", "");
        }
    }

    private static readonly DateTimeOffset T0 = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.NewGuid();

    private static RunJobChainHandler RunHandler(InMemoryJobChainRepository chains, InMemoryJobRepository jobs,
        FakeRunDispatcher dispatcher, InMemoryChainRunRepository? runs = null,
        IClientCommunicationSender? communications = null,
        IPermissionService? permissions = null, IChainQuestionGenerator? questions = null, IOutcomeReviewer? jev = null)
        => new(chains, jobs, runs ?? new InMemoryChainRunRepository(),
            new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(dispatcher),
            communications: communications, permissions: permissions, questions: questions, jev: jev);

    private static Job MakeJob(string name, Guid? projectId = null)
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return Job.Create(projectId ?? ProjectId, name, null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
    }

    // ── definition ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_validates_jobs_in_nested_false_paths(bool missing)
    {
        var jobs = new InMemoryJobRepository();
        var first = MakeJob("first");
        var foreign = MakeJob("foreign", Guid.NewGuid());
        await jobs.AddAsync(first);
        if (!missing)
        {
            await jobs.AddAsync(foreign);
        }
        var uow = new RecordingUnitOfWork();
        var handler = new CreateJobChainHandler(new InMemoryJobChainRepository(), jobs, uow, new FakeClock(T0));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new CreateJobChainCommand(
            ProjectId, "conditional", null, [first.Id], Stages: [[first.Id]],
            StageGates: [new ConditionGate("exists:ok", [new ChainStage([first.Id],
                new ConditionGate("exists:backup", [ChainStage.Of(foreign.Id)]))])])));
        Assert.Contains(missing ? "unknown job" : "different project", error.Message);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Create_persists_the_chain_and_resolves_step_names()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract");
        var b = MakeJob("report");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var handler = new CreateJobChainHandler(chains, jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var view = await handler.HandleAsync(new CreateJobChainCommand(ProjectId, "nightly", "e2e", new[] { a.Id, b.Id }));

        Assert.Equal(new[] { "extract", "report" }, view.Steps.Select(s => s.JobName));
        Assert.NotNull(await chains.GetByIdAsync(view.Id));
    }

    [Fact]
    public async Task Create_rejects_unknown_jobs_and_jobs_from_other_projects()
    {
        var jobs = new InMemoryJobRepository();
        var foreign = MakeJob("other", Guid.NewGuid());
        await jobs.AddAsync(foreign);
        var handler = new CreateJobChainHandler(new InMemoryJobChainRepository(), jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateJobChainCommand(ProjectId, "c", null, new[] { Guid.NewGuid() })));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new CreateJobChainCommand(ProjectId, "c", null, new[] { foreign.Id })));
    }

    [Fact]
    public async Task Updating_a_workflow_rejects_stale_editor_revisions_without_mutating_the_definition()
    {
        var jobs = new InMemoryJobRepository();
        var first = MakeJob("initial");
        var second = MakeJob("updated");
        await jobs.AddAsync(first);
        await jobs.AddAsync(second);
        var chains = new InMemoryJobChainRepository();
        var initial = JobChain.Create(ProjectId, "pipeline", null, [first.Id], T0);
        await chains.AddAsync(initial);
        var handler = new UpdateJobChainHandler(chains, jobs, new RecordingUnitOfWork(), new FakeClock(T0));

        var updated = await handler.HandleAsync(new UpdateJobChainCommand(
            initial.Id, "new pipeline", null, [second.Id], ExpectedUpdatedAt: T0));
        Assert.True(updated.UpdatedAt > T0);
        Assert.Equal("new pipeline", updated.Name);

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new UpdateJobChainCommand(
                initial.Id, "stale overwrite", null, [first.Id], ExpectedUpdatedAt: T0)));
        Assert.Contains("Reload before saving", stale.Message);
        var current = await chains.GetByIdAsync(initial.Id);
        Assert.NotNull(current);
        Assert.Equal("new pipeline", current!.Name);
        Assert.Equal(new[] { second.Id }, current.StepJobIds);
    }

    [Fact]
    public void Workflow_domain_keeps_its_loaded_revision_across_mutations()
    {
        var original = JobChain.Create(ProjectId, "pipeline", null, [Guid.NewGuid()], T0);
        Assert.Equal(T0, original.OriginalUpdatedAt);
        original.Update("after", null, [Guid.NewGuid()], T0.AddTicks(10));
        Assert.Equal(T0, original.OriginalUpdatedAt);
        Assert.True(original.UpdatedAt > original.OriginalUpdatedAt);
    }

    [Fact]
    public void Chain_requires_a_name_and_at_least_one_step()
    {
        Assert.Throws<ArgumentException>(() => JobChain.Create(ProjectId, " ", null, new[] { Guid.NewGuid() }, T0));
        Assert.Throws<ArgumentException>(() => JobChain.Create(ProjectId, "c", null, Array.Empty<Guid>(), T0));
    }

    // ── running ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_threads_each_steps_output_into_the_next_step()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract");
        var b = MakeJob("report");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{\"rows\":3}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"report\":\"done\"}" });

        var view = await RunHandler(chains, jobs, dispatcher)
            .HandleAsync(new RunJobChainCommand(chain.Id, "{\"from\":\"caller\"}"));

        Assert.Equal("Succeeded", view.Status);
        Assert.Equal(2, view.Steps.Count);
        // Downstream stages retain accumulated JSON context, including the
        // caller inputs, so human gates can inspect earlier evidence.
        Assert.Equal("{\"from\":\"caller\"}", dispatcher.Payloads[0]);
        using var secondInput = System.Text.Json.JsonDocument.Parse(dispatcher.Payloads[1]!);
        Assert.Equal("caller", secondInput.RootElement.GetProperty("from").GetString());
        Assert.Equal(3, secondInput.RootElement.GetProperty("rows").GetInt32());
        using var finalContext = System.Text.Json.JsonDocument.Parse(view.FinalOutput!);
        Assert.Equal("caller", finalContext.RootElement.GetProperty("from").GetString());
        Assert.Equal(3, finalContext.RootElement.GetProperty("rows").GetInt32());
        Assert.Equal("done", finalContext.RootElement.GetProperty("report").GetString());
    }

    [Fact]
    public async Task Run_stops_at_the_first_failed_step()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var b = MakeJob("b");
        var c = MakeJob("c");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        await jobs.AddAsync(c);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id, c.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Failed");

        var runs = new InMemoryChainRunRepository();
        var view = await RunHandler(chains, jobs, dispatcher, runs).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Failed", view.Status);
        Assert.Equal(3, view.Steps.Count);                 // every stage is on the run...
        Assert.Equal("Failed", view.Steps[1].Status);
        Assert.Equal("Skipped", view.Steps[2].Status);     // ...steps after the failure are Skipped
        // The persisted progression is what the portal's live pipeline view observes.
        Assert.Contains("Running,Pending,Pending", runs.SavedStepSnapshots);
        Assert.Contains("Succeeded,Running,Pending", runs.SavedStepSnapshots);
        Assert.Equal("Succeeded,Failed,Skipped", runs.SavedStepSnapshots[^1]);
    }

    [Fact]
    public async Task Run_prefers_the_reduce_artifact_and_wraps_multiple_shards_as_a_json_array()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("map-reduce");
        var b = MakeJob("fanout");
        var c = MakeJob("sink");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        await jobs.AddAsync(c);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id, c.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded",
            shardArtifacts: new[] { "{\"ignored\":true}" }, reduceArtifact: "{\"total\":10}");
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"n\":1}", "plain text" });
        dispatcher.Results[c.Id] = Run(c.Id, "Succeeded", shardArtifacts: new[] { "{}" });

        await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        // Reduce wins over shards; no "previous": null is injected for the
        // first stage of a workflow started without an upstream payload.
        Assert.Equal("{\"total\":10}", dispatcher.Payloads[1]);
        // Multiple shard outputs appear in a "branches" context envelope.
        using var wrapped = System.Text.Json.JsonDocument.Parse(dispatcher.Payloads[2]!);
        Assert.Equal(10, wrapped.RootElement.GetProperty("total").GetInt32());
        var branches = wrapped.RootElement.GetProperty("branches");
        Assert.Equal(2, branches.GetArrayLength());
        Assert.Equal(1, branches[0].GetProperty("n").GetInt32());
        Assert.Equal("plain text", branches[1].GetString());
    }

    [Fact]
    public async Task Run_honors_a_preallocated_chain_run_id_and_preallocates_each_steps_run_id()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract");
        var b = MakeJob("report");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{}" });

        var chainRunId = Guid.NewGuid();
        var view = await RunHandler(chains, jobs, dispatcher)
            .HandleAsync(new RunJobChainCommand(chain.Id, null, chainRunId));

        // The caller's id names the run (so its tracking can correlate before the handler returns)…
        Assert.Equal(chainRunId, view.Id);
        // …and every step's RunJobCommand carried a pre-allocated, distinct run id.
        Assert.Equal(2, dispatcher.RunIds.Count);
        Assert.All(dispatcher.RunIds, id => Assert.NotNull(id));
        Assert.NotEqual(dispatcher.RunIds[0], dispatcher.RunIds[1]);
    }

    [Fact]
    public void MarkStepRunning_records_the_steps_run_id_up_front()
    {
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { Guid.NewGuid() }, T0);
        var run = ChainRun.Start(chain, new[] { "extract" }, T0);
        var stepRunId = Guid.NewGuid();

        run.MarkStepRunning(0, stepRunId, T0.AddSeconds(1));

        Assert.Equal(stepRunId, run.Steps[0].RunId); // a live pipeline can link to the running step
        Assert.Equal(ChainStepStatus.Running, run.Steps[0].Status);
    }

    [Fact]
    public async Task Send_email_action_substitutes_previous_output_and_records_delivery()
    {
        var jobs = new InMemoryJobRepository();
        var report = MakeJob("report");
        await jobs.AddAsync(report);
        var chains = new InMemoryJobChainRepository();
        var email = new SendEmailChainAction(
            "{{client.email}}", "{{client.name}}", "Report for {{site.address}}",
            "Hello {{client.name}}, your report score is {{score}}.");
        var chain = JobChain.Create(ProjectId, "deliver", null,
            new[] { ChainStage.Of(report.Id), ChainStage.ForAction(email) }, T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[report.Id] = Run(report.Id, "Succeeded", shardArtifacts: new[]
        {
            """{"client":{"email":"client@example.com","name":"Alex"},"site":{"address":"12 Smith St"},"score":92,"release":{"email_release":true,"client_release":true}}"""
        });
        var sender = new FakeCommunicationSender();

        var view = await RunHandler(chains, jobs, dispatcher,
                communications: sender, permissions: new FakePermissionService(true))
            .HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        var sent = Assert.Single(sender.Emails);
        Assert.Equal("client@example.com", sent.Recipient);
        Assert.Equal("Alex", sent.RecipientName);
        Assert.Equal("Report for 12 Smith St", sent.Subject);
        Assert.Equal("Hello Alex, your report score is 92.", sent.Body);
        var actionStep = view.Steps[1];
        Assert.Equal(SendEmailChainAction.ActionType, actionStep.ActionType);
        Assert.Equal("Postmark", actionStep.Provider);
        Assert.Equal("message-123", actionStep.ExternalId);
        Assert.Null(actionStep.Error);
    }

    [Fact]
    public async Task Wait_gate_persists_state_and_resume_continues_the_same_run_without_delaying()
    {
        var jobs = new InMemoryJobRepository();
        var first = MakeJob("first");
        var second = MakeJob("second");
        await jobs.AddAsync(first);
        await jobs.AddAsync(second);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "durable wait", null,
            new[]
            {
                ChainStage.Of(first.Id),
                new ChainStage(new[] { second.Id }, new WaitGate(TimeSpan.FromHours(2))),
            }, T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[first.Id] = Run(first.Id, "Succeeded", shardArtifacts: new[] { "{\"first\":true}" });
        dispatcher.Results[second.Id] = Run(second.Id, "Succeeded", shardArtifacts: new[] { "{\"done\":true}" });
        var runs = new InMemoryChainRunRepository();
        var handler = RunHandler(chains, jobs, dispatcher, runs);

        var waiting = await handler.HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Waiting", waiting.Status);
        Assert.Equal(T0.AddHours(2), (await runs.GetByIdAsync(waiting.Id))!.ResumeAt);
        Assert.Single(dispatcher.Payloads);

        var completed = await handler.HandleAsync(new RunJobChainCommand(
            chain.Id, waiting.FinalOutput, waiting.Id, ResumeFromStageIndex: 1));

        Assert.Equal(waiting.Id, completed.Id);
        Assert.Equal("Succeeded", completed.Status);
        Assert.Equal("{\"first\":true}", dispatcher.Payloads[1]);
        Assert.All(completed.Steps, step => Assert.Equal("Succeeded", step.Status));
    }

    [Fact]
    public async Task Send_email_action_resolves_binary_attachments_from_previous_json()
    {
        var jobs = new InMemoryJobRepository();
        var report = MakeJob("report");
        await jobs.AddAsync(report);
        var chains = new InMemoryJobChainRepository();
        var email = new SendEmailChainAction(
            "client@example.com", "Client", "Your report", "Attached.", "{{attachments}}");
        var chain = JobChain.Create(ProjectId, "deliver", null,
            new[] { ChainStage.Of(report.Id), ChainStage.ForAction(email) }, T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[report.Id] = Run(report.Id, "Succeeded", shardArtifacts: new[]
        {
            """{"attachments":[{"name":"report.pdf","contentBase64":"UERG","contentType":"application/pdf"}]}"""
        });
        var sender = new FakeCommunicationSender();

        var view = await RunHandler(chains, jobs, dispatcher,
                communications: sender, permissions: new FakePermissionService(true))
            .HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        var attachment = Assert.Single(Assert.Single(sender.Emails).Attachments);
        Assert.Equal("report.pdf", attachment.Name);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal("UERG", attachment.ContentBase64);
    }

    [Fact]
    public void Email_attachment_mapping_rejects_urls_and_invalid_binary_content()
    {
        var urlError = Assert.Throws<InvalidOperationException>(() =>
            RunJobChainHandler.ResolveEmailAttachments("attachment",
                """{"attachment":{"name":"report.pdf","url":"http://internal/report.pdf"}}"""));
        Assert.Contains("missing its content", urlError.Message);

        var base64Error = Assert.Throws<InvalidOperationException>(() =>
            RunJobChainHandler.ResolveEmailAttachments("attachment",
                """{"attachment":{"name":"report.pdf","contentBase64":"not base64"}}"""));
        Assert.Contains("not valid base64", base64Error.Message);
    }

    [Fact]
    public async Task Send_email_rejection_fails_action_and_skips_later_stages()
    {
        var jobs = new InMemoryJobRepository();
        var later = MakeJob("later");
        await jobs.AddAsync(later);
        var chains = new InMemoryJobChainRepository();
        var email = new SendEmailChainAction(
            "client@example.com", "Client", "Report", "Body");
        var chain = JobChain.Create(ProjectId, "deliver", null,
            new[] { ChainStage.ForAction(email), ChainStage.Of(later.Id) }, T0);
        await chains.AddAsync(chain);
        var sender = new FakeCommunicationSender
        {
            Failure = new InvalidOperationException("Postmark rejected the message (422).")
        };
        var dispatcher = new FakeRunDispatcher();

        var view = await RunHandler(chains, jobs, dispatcher,
                communications: sender, permissions: new FakePermissionService(true))
            .HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Failed", view.Status);
        Assert.Equal("Failed", view.Steps[0].Status);
        Assert.Contains("Postmark rejected", view.Steps[0].Error);
        Assert.Equal("Skipped", view.Steps[1].Status);
        Assert.Empty(dispatcher.DispatchedJobIds);
    }

    [Fact]
    public async Task Send_email_requires_permission_and_explicit_release_when_flags_exist()
    {
        var jobs = new InMemoryJobRepository();
        var chains = new InMemoryJobChainRepository();
        var email = new SendEmailChainAction(
            "client@example.com", "Client", "Report", "Body");
        var chain = JobChain.Create(ProjectId, "deliver", null,
            new[] { ChainStage.ForAction(email) }, T0);
        await chains.AddAsync(chain);
        var sender = new FakeCommunicationSender();

        var denied = await RunHandler(chains, jobs, new FakeRunDispatcher(),
                communications: sender, permissions: new FakePermissionService(false))
            .HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("Failed", denied.Status);
        Assert.Contains(Permission.EmailSend, denied.Steps[0].Error);
        Assert.Empty(sender.Emails);

        var releaseBlocked = await RunHandler(chains, jobs, new FakeRunDispatcher(),
                communications: sender, permissions: new FakePermissionService(true))
            .HandleAsync(new RunJobChainCommand(chain.Id,
                """{"release":{"email_release":false,"client_release":false}}"""));
        Assert.Equal("Failed", releaseBlocked.Status);
        Assert.Contains("release object", releaseBlocked.Steps[0].Error);
        Assert.Empty(sender.Emails);
    }

    [Fact]
    public async Task Send_sms_action_substitutes_payload_and_records_provider_delivery()
    {
        var jobs = new InMemoryJobRepository();
        var chains = new InMemoryJobChainRepository();
        var sms = new SendSmsChainAction("{{client.phone}}", "Hi {{client.name}}, report {{report.id}} is ready.");
        var chain = JobChain.Create(ProjectId, "notify", null,
            new[] { ChainStage.ForAction(sms) }, T0);
        await chains.AddAsync(chain);
        var sender = new FakeCommunicationSender();

        var view = await RunHandler(chains, jobs, new FakeRunDispatcher(),
                communications: sender, permissions: new FakePermissionService(true))
            .HandleAsync(new RunJobChainCommand(chain.Id,
                """{"client":{"phone":"+61412345678","name":"Alex"},"report":{"id":"R-42"},"release":{"sms_release":true,"client_release":true}}"""));

        Assert.Equal("Succeeded", view.Status);
        var sent = Assert.Single(sender.SmsMessages);
        Assert.Equal("+61412345678", sent.Recipient);
        Assert.Equal("Hi Alex, report R-42 is ready.", sent.Body);
        Assert.Equal("Twilio", view.Steps[0].Provider);
        Assert.Equal("sms-123", view.Steps[0].ExternalId);
    }

    private sealed class FakeCommunicationSender : IClientCommunicationSender
    {
        public string EmailProvider => "Postmark";
        public string SmsProvider => "Twilio";
        public Exception? Failure
        {
            get; init;
        }
        public List<(string Recipient, string RecipientName, string Subject, string Body,
            IReadOnlyList<ClientEmailAttachment> Attachments)> Emails
        {
            get;
        } = new();
        public List<(string Recipient, string Body)> SmsMessages { get; } = new();

        public Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(new ClientCommsCapabilities(true, false, EmailProvider, SmsProvider));

        public Task<ClientMessageDelivery> SendEmailAsync(string recipient, string recipientName,
            string subject, string body, CancellationToken ct = default,
            IReadOnlyList<ClientEmailAttachment>? attachments = null)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            Emails.Add((recipient, recipientName, subject, body,
                            attachments ?? Array.Empty<ClientEmailAttachment>()));
            return Task.FromResult(new ClientMessageDelivery("Postmark", "message-123"));
        }

        public Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
            string recipient, string recipientName, string subject, string body,
            CancellationToken ct = default)
            => Task.FromResult(new ClientMessageDelivery("Postmark", "auth-message-123"));

        public Task<ClientMessageDelivery> SendSmsAsync(
            string recipient, string body, CancellationToken ct = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            SmsMessages.Add((recipient, body));
            return Task.FromResult(new ClientMessageDelivery("Twilio", "sms-123"));
        }

        public Task<ClientMessageDelivery> SendAuthenticationSmsAsync(
            string recipient, string body, CancellationToken ct = default)
            => Task.FromResult(new ClientMessageDelivery("Twilio", "auth-sms-123"));
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _allowed;
        public FakePermissionService(bool allowed) => _allowed = allowed;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default)
            => Task.FromResult(_allowed
                && permission is Permission.EmailSend or Permission.SmsSend or Permission.JobsRun or Permission.DataRead);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(
            Guid userId, string roleName, CancellationToken ct = default)
            => GetEffectivePermissionsAsync(ct);
    }

    // ── fan-out / fan-in ────────────────────────────────────────────────────────────────────────────

    private static JobChain FanOutJoinChain(Guid a, Guid b1, Guid b2, Guid join)
        => JobChain.Create(ProjectId, "fan-out-join", null,
            new[] { ChainStage.Of(a), new ChainStage(new[] { b1, b2 }), ChainStage.Of(join) }, T0);

    [Fact]
    public async Task Run_dispatches_a_fan_out_stages_branches_concurrently()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var b1 = MakeJob("b1");
        var b2 = MakeJob("b2");
        var join = MakeJob("join");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b1);
        await jobs.AddAsync(b2);
        await jobs.AddAsync(join);
        var chains = new InMemoryJobChainRepository();
        var chain = FanOutJoinChain(a.Id, b1.Id, b2.Id, join.Id);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        // Both branches sleep briefly so their execution windows overlap if — and only if — they
        // were genuinely dispatched concurrently rather than one after another.
        dispatcher.Delays[b1.Id] = TimeSpan.FromMilliseconds(60);
        dispatcher.Delays[b2.Id] = TimeSpan.FromMilliseconds(60);
        dispatcher.Results[b1.Id] = Run(b1.Id, "Succeeded", shardArtifacts: new[] { "{\"b\":1}" });
        dispatcher.Results[b2.Id] = Run(b2.Id, "Succeeded", shardArtifacts: new[] { "{\"b\":2}" });
        dispatcher.Results[join.Id] = Run(join.Id, "Succeeded", shardArtifacts: new[] { "{\"joined\":true}" });

        var view = await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        Assert.True(dispatcher.MaxObservedConcurrency >= 2, "the fan-out branches should overlap in flight");
    }

    [Fact]
    public async Task Run_caps_fan_out_concurrency_at_a_conservative_bound()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var branchJobs = Enumerable.Range(0, 8).Select(i => MakeJob($"b{i}")).ToList();
        var join = MakeJob("join");
        await jobs.AddAsync(a);
        foreach (var b in branchJobs)
        {
            await jobs.AddAsync(b);
        }
        await jobs.AddAsync(join);

        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "wide-fan-out", null,
            new[] { ChainStage.Of(a.Id), new ChainStage(branchJobs.Select(b => b.Id)), ChainStage.Of(join.Id) }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        foreach (var b in branchJobs)
        {
            dispatcher.Delays[b.Id] = TimeSpan.FromMilliseconds(40);
            dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        }
        dispatcher.Results[join.Id] = Run(join.Id, "Succeeded", shardArtifacts: new[] { "{}" });

        await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.InRange(dispatcher.MaxObservedConcurrency, 2, 4); // bounded, not unlimited fan-out
    }

    [Fact]
    public async Task Run_threads_every_branchs_output_into_the_join()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var b1 = MakeJob("b1");
        var b2 = MakeJob("b2");
        var join = MakeJob("join");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b1);
        await jobs.AddAsync(b2);
        await jobs.AddAsync(join);
        var chains = new InMemoryJobChainRepository();
        var chain = FanOutJoinChain(a.Id, b1.Id, b2.Id, join.Id);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b1.Id] = Run(b1.Id, "Succeeded", shardArtifacts: new[] { "{\"branch\":1}" });
        dispatcher.Results[b2.Id] = Run(b2.Id, "Succeeded", shardArtifacts: new[] { "{\"branch\":2}" });
        dispatcher.Results[join.Id] = Run(join.Id, "Succeeded", shardArtifacts: new[] { "{\"joined\":true}" });

        var view = await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        // The join receives both branches in-order in the cumulative context.
        var joinPayload = dispatcher.Payloads[dispatcher.DispatchedJobIds.IndexOf(join.Id)];
        using var input = System.Text.Json.JsonDocument.Parse(joinPayload!);
        var branches = input.RootElement.GetProperty("branches");
        Assert.Equal(2, branches.GetArrayLength());
        Assert.Equal(1, branches[0].GetProperty("branch").GetInt32());
        Assert.Equal(2, branches[1].GetProperty("branch").GetInt32());
        using var final = System.Text.Json.JsonDocument.Parse(view.FinalOutput!);
        Assert.True(final.RootElement.GetProperty("joined").GetBoolean());
        Assert.Equal(2, final.RootElement.GetProperty("branches").GetArrayLength());
    }

    [Fact]
    public async Task Run_any_branch_failure_fails_the_chain_and_skips_the_join()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var b1 = MakeJob("b1");
        var b2 = MakeJob("b2");
        var join = MakeJob("join");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b1);
        await jobs.AddAsync(b2);
        await jobs.AddAsync(join);
        var chains = new InMemoryJobChainRepository();
        var chain = FanOutJoinChain(a.Id, b1.Id, b2.Id, join.Id);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b1.Id] = Run(b1.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b2.Id] = Run(b2.Id, "Failed");
        // No result registered for `join` — if it were dispatched anyway, the lookup would throw.

        var view = await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Failed", view.Status);
        Assert.DoesNotContain(join.Id, dispatcher.DispatchedJobIds); // the join never ran
        var byId = view.Steps.ToDictionary(s => s.JobId);
        Assert.Equal("Succeeded", byId[a.Id].Status);
        Assert.Equal("Succeeded", byId[b1.Id].Status);
        Assert.Equal("Failed", byId[b2.Id].Status);
        Assert.Equal("Skipped", byId[join.Id].Status);
        // Every step of the fan-out stage shares a stage index; the join is the very next stage.
        Assert.Equal(view.Steps.First(s => s.JobId == b1.Id).StageIndex, view.Steps.First(s => s.JobId == b2.Id).StageIndex);
        Assert.Equal(view.Steps.First(s => s.JobId == b1.Id).StageIndex + 1, view.Steps.First(s => s.JobId == join.Id).StageIndex);
    }

    [Fact]
    public async Task Run_a_partial_branch_downgrades_the_chain_but_the_join_still_runs()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("a");
        var b1 = MakeJob("b1");
        var b2 = MakeJob("b2");
        var join = MakeJob("join");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b1);
        await jobs.AddAsync(b2);
        await jobs.AddAsync(join);
        var chains = new InMemoryJobChainRepository();
        var chain = FanOutJoinChain(a.Id, b1.Id, b2.Id, join.Id);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b1.Id] = Run(b1.Id, "Partial", shardArtifacts: new[] { "{}" });
        dispatcher.Results[b2.Id] = Run(b2.Id, "Succeeded", shardArtifacts: new[] { "{}" });
        dispatcher.Results[join.Id] = Run(join.Id, "Succeeded", shardArtifacts: new[] { "{\"joined\":true}" });

        var view = await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Partial", view.Status);
        Assert.Contains(join.Id, dispatcher.DispatchedJobIds); // a Partial branch doesn't halt the chain
        Assert.Equal("Succeeded", view.Steps.First(s => s.JobId == join.Id).Status);
    }

    [Fact]
    public async Task Run_a_linear_chain_is_every_stage_size_one_and_behaves_exactly_as_before()
    {
        // A chain authored via the flat (pre-fan-out) API is indistinguishable at run time from one
        // built out of explicit size-1 stages — the backward-compatibility guarantee.
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract");
        var b = MakeJob("report");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id }, T0);
        await chains.AddAsync(chain);
        Assert.All(chain.Stages, s => Assert.False(s.IsParallel));

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded", shardArtifacts: new[] { "{\"rows\":3}" });
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"report\":\"done\"}" });

        var view = await RunHandler(chains, jobs, dispatcher).HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        Assert.All(view.Steps, s => Assert.Equal(s.Index, s.StageIndex)); // every step is its own stage
        Assert.All(view.Steps, s => Assert.Equal(0, s.BranchIndex));
    }

    [Fact]
    public async Task Run_falls_back_to_named_artifacts_when_stdout_is_null()
    {
        var jobs = new InMemoryJobRepository();
        var a = MakeJob("extract");
        var b = MakeJob("report");
        await jobs.AddAsync(a);
        await jobs.AddAsync(b);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "pipeline", null, new[] { a.Id, b.Id }, T0);
        await chains.AddAsync(chain);

        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[a.Id] = Run(a.Id, "Succeeded",
            shardArtifacts: new[] { "" },
            shardNamedArtifacts: new[] { new[] { new RunArtifactView("report.html", "<h1>Report</h1>") } });
        dispatcher.Results[b.Id] = Run(b.Id, "Succeeded", shardArtifacts: new[] { "{\"ok\":true}" });

        var view = await RunHandler(chains, jobs, dispatcher)
            .HandleAsync(new RunJobChainCommand(chain.Id));

        Assert.Equal("Succeeded", view.Status);
        Assert.Equal("<h1>Report</h1>", dispatcher.Payloads[1]);
        Assert.Equal("{\"ok\":true}", view.FinalOutput);
    }

    [Fact]
    public void PrimaryOutput_uses_named_artifacts_when_no_stdout()
    {
        var run = Run(Guid.NewGuid(), "Succeeded",
            shardArtifacts: new[] { "", "" },
            shardNamedArtifacts: new[]
            {
                new[] { new RunArtifactView("data.json", "{\"k\":1}") },
                new[] { new RunArtifactView("log.txt", "all good") },
            });
        Assert.Equal("[{\"k\":1},\"all good\"]", RunJobChainHandler.PrimaryOutput(run));
    }

    [Fact]
    public void PrimaryOutput_returns_null_when_no_artifacts_at_all()
    {
        var run = Run(Guid.NewGuid(), "Succeeded");
        Assert.Null(RunJobChainHandler.PrimaryOutput(run));
    }

    [Fact]
    public async Task Missing_stage_input_pauses_durably_and_resumes_without_replaying_prior_steps()
    {
        var jobs = new InMemoryJobRepository();
        var first = MakeJob("property-analysis");
        var funding = Job.Create(ProjectId, "funding-position", null,
            new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>()), null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0,
            parameters: new[] { new JobParameter("funding_inputs.client_cash", type: "number"),
                new JobParameter("funding_inputs.purchase_price", type: "number") });
        var renderer = MakeJob("renderer");
        foreach (var job in new[] { first, funding, renderer })
        {
            await jobs.AddAsync(job);
        }
        var chain = JobChain.Create(ProjectId, "report", null, new[] { first.Id, funding.Id, renderer.Id }, T0);
        var chains = new InMemoryJobChainRepository();
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[first.Id] = Run(first.Id, "Succeeded", shardArtifacts: new[] { "{\"site\":{\"address\":\"retained\"},\"funding_inputs\":{\"purchase_price\":100}}" });
        dispatcher.Results[funding.Id] = Run(funding.Id, "Succeeded", shardArtifacts: new[] { "{\"funding\":true}" });
        dispatcher.Results[renderer.Id] = Run(renderer.Id, "Succeeded", shardArtifacts: new[] { "{\"pdf\":true}" });
        var paused = await RunHandler(chains, jobs, dispatcher, runs).HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("AwaitingInput", paused.Status);
        Assert.Null(paused.FinishedAt);
        Assert.Equal(new[] { first.Id }, dispatcher.DispatchedJobIds);
        Assert.Equal(new[] { "Succeeded", "Pending", "Pending" }, paused.Steps.Select(step => step.Status));
        var request = Assert.IsType<ChainInputRequest>(paused.InputRequest);
        var stored = (await runs.GetByIdAsync(paused.Id))!;
        Assert.Null(stored.ResumeAt);
        Assert.Equal(1, stored.ResumeStageIndex);
        Assert.Throws<ArgumentException>(() => ChainInputPayload.Apply(request, "{\"1\":{}}", null, stored.FinalOutput));
        Assert.Throws<ArgumentException>(() => ChainInputPayload.Apply(request, "{\"1\":{\"unrelated\":1}}", null, stored.FinalOutput));
        Assert.Throws<ArgumentException>(() => ChainInputPayload.Apply(request, "{\"1\":{\"funding_inputs.client_cash\":\"NaN\"}}", null, stored.FinalOutput));
        var overrides = ChainInputPayload.Apply(request, "{\"1\":{\"funding_inputs.client_cash\":0}}", null, stored.FinalOutput);
        // Simulate the durable queue scheduling the checkpoint after validated input submission.
        stored.Pause(1, stored.FinalOutput, T0, System.Text.Json.JsonSerializer.Serialize(overrides));
        var resumed = await RunHandler(chains, jobs, dispatcher, runs).HandleAsync(
            new RunJobChainCommand(chain.Id, ChainRunId: stored.Id, StepPayloadOverrides: overrides, ResumeFromStageIndex: 1));
        Assert.Equal(paused.Id, resumed.Id);
        Assert.Equal("Succeeded", resumed.Status);
        Assert.Equal(new[] { first.Id, funding.Id, renderer.Id }, dispatcher.DispatchedJobIds);
        using var input = System.Text.Json.JsonDocument.Parse(dispatcher.Payloads[1]!);
        Assert.Equal(0, input.RootElement.GetProperty("funding_inputs").GetProperty("client_cash").GetInt32());
        Assert.Equal(100, input.RootElement.GetProperty("funding_inputs").GetProperty("purchase_price").GetInt32());
        Assert.Equal("retained", input.RootElement.GetProperty("site").GetProperty("address").GetString());
    }

    [Theory]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.TextResponse, true)]
    [InlineData(LoopChainAction.AgentType, LoopChainAction.TextResponse, true)]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.YesNoResponse, true)]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.YesNoResponse, false)]
    public async Task Loop_steps_require_fresh_responses_and_resume_without_repeating_jobs(string kind, string responseType, bool answer)
    {
        var jobs = new InMemoryJobRepository();
        var first = MakeJob("prepare");
        var last = MakeJob("publish");
        await jobs.AddAsync(first);
        await jobs.AddAsync(last);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "review", null, new[]
        {
            ChainStage.Of(first.Id),
            ChainStage.ForAction(new LoopChainAction(kind, "Review the draft", "review", responseType)),
            ChainStage.ForAction(new LoopChainAction(kind, "Review it again", "review", responseType)),
            ChainStage.Of(last.Id),
        }, T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[first.Id] = Run(first.Id, "Succeeded", shardArtifacts: ["{\"draft\":42,\"review\":\"old\"}"]);
        dispatcher.Results[last.Id] = Run(last.Id, "Succeeded");
        var questions = new ValidationQuestions();
        var handler = RunHandler(chains, jobs, dispatcher, runs, questions: questions);
        var paused = await handler.HandleAsync(new RunJobChainCommand(chain.Id,
            StepPayloadOverrides: new Dictionary<int, string> { [1] = "{\"review\":\"prefilled\"}" }));
        var firstRequest = paused.InputRequest!;
        Assert.Equal("AwaitingInput", paused.Status);
        Assert.Equal(kind, firstRequest.Kind);
        Assert.Equal(responseType, firstRequest.ResponseType);
        Assert.Equal("Validate: Review the draft?", firstRequest.Instructions);
        Assert.Contains("\"draft\":42", Assert.Single(questions.Outputs));
        Assert.Single(dispatcher.DispatchedJobIds);

        for (var index = 1; index <= 2; index++)
        {
            var stored = (await runs.GetByIdAsync(paused.Id))!;
            var request = paused.InputRequest!;
            var missing = $"{{\"{index}\":{{}}}}";
            Assert.Throws<ArgumentException>(() => ChainInputPayload.Apply(request, missing, null, stored.FinalOutput));
            foreach (var invalid in new[] { "null", "42", "{}", "\" \"" })
            {
                Assert.Throws<ArgumentException>(() => ChainInputPayload.Apply(request,
                                $"{{\"{index}\":{{\"review\":{invalid}}}}}", null, stored.FinalOutput));
            }
            var response = responseType == LoopChainAction.YesNoResponse ? System.Text.Json.JsonSerializer.Serialize(answer) : $"\"response {index}\"";
            var values = $"{{\"{index}\":{{\"review\":{response}}}}}";
            var overrides = ChainInputPayload.Apply(request, values, stored.ContinuationOverridesJson, stored.FinalOutput);
            // Simulate the queue's persisted, authenticated submission and reload the checkpoint.
            var reloaded = ChainRun.Rehydrate(stored.Id, stored.ChainId, stored.ProjectId, stored.ChainName,
                ChainRunStatus.Waiting, stored.Steps, stored.FinalOutput, stored.StartedAt, null,
                T0, index, System.Text.Json.JsonSerializer.Serialize(overrides),
                System.Text.Json.JsonSerializer.Serialize(request with
                {
                    SubmittedBy = Guid.NewGuid(),
                    SubmittedValues = values
                }));
            await runs.UpdateAsync(reloaded);
            paused = await handler.HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: stored.Id,
                StepPayloadOverrides: overrides, ResumeFromStageIndex: index));
            if (index == 1)
            {
                Assert.Equal("AwaitingInput", paused.Status);
                Assert.NotEqual(firstRequest.Id, paused.InputRequest!.Id);
                Assert.Equal("Validate: Review it again?", paused.InputRequest.Instructions);
                Assert.Single(dispatcher.DispatchedJobIds);
            }
        }
        Assert.Equal("Succeeded", paused.Status);
        Assert.Equal(new[] { first.Id, last.Id }, dispatcher.DispatchedJobIds);
        using var output = System.Text.Json.JsonDocument.Parse(dispatcher.Payloads[1]!);
        Assert.Equal(42, output.RootElement.GetProperty("draft").GetInt32());
        void AssertResponse()
        {
            if (responseType == LoopChainAction.YesNoResponse)
            {
                Assert.Equal(answer, output.RootElement.GetProperty("review").GetBoolean());
                return;
            }
            Assert.Equal("response 2", output.RootElement.GetProperty("review").GetString());
        }
        AssertResponse();
        Assert.All(paused.Steps, step => Assert.Equal("Succeeded", step.Status));
        Assert.Equal(2, questions.Outputs.Count);
    }

    [Fact]
    public async Task Failed_question_generation_stops_the_workflow_before_following_jobs()
    {
        var jobs = new InMemoryJobRepository();
        var publish = MakeJob("publish");
        await jobs.AddAsync(publish);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "review", null, new[]
        {
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.HumanType, "Review the output", "review")),
            ChainStage.Of(publish.Id),
        }, T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        var result = await RunHandler(chains, jobs, dispatcher, runs, questions: new ValidationQuestions(fail: true))
            .HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("Failed", result.Status);
        Assert.Null(result.InputRequest);
        Assert.Equal("Failed", result.Steps[0].Status);
        Assert.Contains("Could not generate validation question", result.Steps[0].Error);
        Assert.Empty(dispatcher.DispatchedJobIds);
        Assert.Equal(ChainRunStatus.Failed, (await runs.GetByIdAsync(result.Id))!.Status);
    }

    [Theory]
    [InlineData(0.95, false)]
    [InlineData(0.1, false)]
    [InlineData(0.5, false)]
    [InlineData(0.95, true)]
    [InlineData(0.1, true)]
    public async Task Automatic_if_reviews_current_data_runs_one_path_and_rejoins(double probability, bool nested)
    {
        var jobs = new InMemoryJobRepository();
        var yes = MakeJob("yes"); var no = MakeJob("no"); var join = MakeJob("join");
        foreach (var job in new[] { yes, no, join }) await jobs.AddAsync(job);
        var automatic = new ChainStage([yes.Id], new ConditionGate(ConditionGate.AutomaticExpression,
            [ChainStage.Of(no.Id)], "Does this need technical support?"));
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "Automatic routing", null,
            [nested ? new ChainStage([yes.Id], new ConditionGate("false", [automatic])) : automatic,
             ChainStage.Of(join.Id)], T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        foreach (var job in new[] { yes, no, join }) dispatcher.Results[job.Id] = Run(job.Id, "Succeeded");
        var reviewer = new FakeOutcomeReviewer(probability);
        var result = await RunHandler(chains, jobs, dispatcher, jev: reviewer)
            .HandleAsync(new(chain.Id, "{\"question\":\"Cannot sign in\",\"ai_gate_review\":{\"noul\":1}}"));
        Assert.Equal("Succeeded", result.Status); Assert.Null(result.InputRequest);
        Assert.Equal(new[] { probability >= 0.5 ? yes.Id : no.Id, join.Id }, dispatcher.DispatchedJobIds);
        var request = Assert.Single(reviewer.Requests);
        Assert.Equal("Does this need technical support?", request.Question);
        Assert.Contains("Cannot sign in", request.State);
        using var output = System.Text.Json.JsonDocument.Parse(result.FinalOutput!);
        var review = output.RootElement.GetProperty("ai_gate_review");
        Assert.Equal(probability, review.GetProperty("noul").GetDouble());
        Assert.Equal(probability >= 0.5 ? "true" : "false", review.GetProperty("path").GetString());
        Assert.Equal("jev-test", review.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("error", false)]
    [InlineData("invalid", false)]
    [InlineData("error", true)]
    [InlineData("invalid", true)]
    public async Task Automatic_if_fails_without_running_either_path_when_review_is_unavailable_or_invalid(string failure, bool nested)
    {
        var jobs = new InMemoryJobRepository(); var yes = MakeJob("yes"); var no = MakeJob("no");
        await jobs.AddAsync(yes); await jobs.AddAsync(no);
        var automatic = new ChainStage([yes.Id], new ConditionGate(ConditionGate.AutomaticExpression,
            [ChainStage.Of(no.Id)], "Ready?"));
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "Automatic routing", null,
            [nested ? new ChainStage([yes.Id], new ConditionGate("false", [automatic])) : automatic, ChainStage.Of(yes.Id)], T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        var result = await RunHandler(chains, jobs, dispatcher, jev: failure == "missing" ? null
            : new FakeOutcomeReviewer(failure == "invalid" ? 2 : 0.9, fail: failure == "error"))
            .HandleAsync(new(chain.Id, "{\"ai_gate_review\":{\"noul\":1}}"));
        Assert.Equal("Failed", result.Status);
        Assert.Contains("Could not choose an automatic if path", Assert.Single(result.Steps, step => step.Status == "Failed").Error);
        Assert.Empty(dispatcher.DispatchedJobIds);
    }

    [Theory]
    [InlineData(0.95, true)]
    [InlineData(0.5, false)]
    [InlineData(0.1, false)]
    public async Task Jev_verifies_previous_output_and_explicit_gate_controls_following_jobs(double probability, bool shouldPublish)
    {
        var jobs = new InMemoryJobRepository();
        var prepare = MakeJob("prepare");
        var publish = MakeJob("publish");
        await jobs.AddAsync(prepare);
        await jobs.AddAsync(publish);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "Jev review", null,
        [
            ChainStage.Of(prepare.Id),
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.AgentType, "Check the total", "review", agentProvider: LoopChainAction.JevAgent)),
            new ChainStage([publish.Id], new ConditionGate("gte:review.noul:0.9")),
        ], T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[prepare.Id] = Run(prepare.Id, "Succeeded", shardArtifacts: ["{\"total\":42}"]);
        dispatcher.Results[publish.Id] = Run(publish.Id, "Succeeded");
        var reviewer = new FakeOutcomeReviewer(probability);
        var runs = new InMemoryChainRunRepository();
        var result = await RunHandler(chains, jobs, dispatcher, runs, questions: new ValidationQuestions(), jev: reviewer)
            .HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("Succeeded", result.Status);
        Assert.Null(result.InputRequest);
        Assert.Equal("Succeeded", result.Steps[1].Status);
        var request = Assert.Single(reviewer.Requests);
        Assert.Equal("Validate: Check the total?", request.Question);
        Assert.Contains("\"total\":42", request.State);
        Assert.Equal(shouldPublish, dispatcher.DispatchedJobIds.Contains(publish.Id));
        if (shouldPublish)
        {
            using var output = System.Text.Json.JsonDocument.Parse(dispatcher.Payloads[1]!);
            Assert.Equal(42, output.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(probability, output.RootElement.GetProperty("review").GetProperty("noul").GetDouble());
            Assert.Equal("jev-test", output.RootElement.GetProperty("review").GetProperty("model").GetString());
            return;
        }
        {
            using var output = System.Text.Json.JsonDocument.Parse((await runs.GetByIdAsync(result.Id))!.FinalOutput!);
            Assert.Equal(probability, output.RootElement.GetProperty("review").GetProperty("noul").GetDouble());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Jev_failure_or_missing_adapter_stops_before_following_jobs(bool configured)
    {
        var jobs = new InMemoryJobRepository();
        var publish = MakeJob("publish");
        await jobs.AddAsync(publish);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "Jev review", null,
        [
            ChainStage.ForAction(new LoopChainAction(LoopChainAction.AgentType, "Check output", "review", agentProvider: LoopChainAction.JevAgent)),
            ChainStage.Of(publish.Id),
        ], T0);
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        var result = await RunHandler(chains, jobs, dispatcher, jev: configured ? new FakeOutcomeReviewer(fail: true) : null)
            .HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("Failed", result.Status);
        Assert.Null(result.InputRequest);
        Assert.Contains("Could not complete AI verification", result.Steps[0].Error);
        Assert.Empty(dispatcher.DispatchedJobIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throttled_AI_retries_same_stage_with_backoff_without_repeating_completed_jobs(bool exhausted)
    {
        var jobs = new InMemoryJobRepository();
        var prepare = MakeJob("prepare");
        var publish = MakeJob("publish");
        await jobs.AddAsync(prepare);
        await jobs.AddAsync(publish);
        var chain = JobChain.Create(ProjectId, "retry review", null,
        [ChainStage.Of(prepare.Id),
         ChainStage.ForAction(new LoopChainAction(LoopChainAction.AgentType, "Check output", "review", agentProvider: LoopChainAction.JevAgent)),
         ChainStage.Of(publish.Id)], T0);
        var chains = new InMemoryJobChainRepository();
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[prepare.Id] = Run(prepare.Id, "Succeeded", shardArtifacts: ["{\"records\":[1,2,3]}"]);
        dispatcher.Results[publish.Id] = Run(publish.Id, "Succeeded");
        var reviewer = new ThrottledReviewer();
        var clock = new FakeClock(T0);
        RunJobChainHandler Handler() => new(chains, jobs, runs, new RecordingUnitOfWork(), clock,
            new FakeJobRunner(dispatcher), jev: reviewer);
        var result = await Handler().HandleAsync(new RunJobChainCommand(chain.Id));
        var payload = (await runs.GetByIdAsync(result.Id))!.FinalOutput;
        for (var retry = 1; retry <= 3; retry++)
        {
            var saved = (await runs.GetByIdAsync(result.Id))!;
            Assert.Equal("Waiting", result.Status);
            Assert.Equal(1, saved.ResumeStageIndex);
            Assert.Null(saved.FinishedAt);
            Assert.Equal(retry, saved.Steps[1].RetryCount);
            Assert.Equal(ChainStepStatus.Pending, saved.Steps[1].Status);
            Assert.Equal(payload, saved.FinalOutput);
            Assert.Single(dispatcher.DispatchedJobIds);
            var minimum = Math.Max(45, 30 * Math.Pow(2, retry - 1));
            Assert.InRange((saved.ResumeAt!.Value - clock.UtcNow).TotalSeconds, minimum, minimum + 1);
            clock.UtcNow = saved.ResumeAt.Value;
            reviewer.Succeed = !exhausted && retry == 3;
            result = await Handler().HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: saved.Id, ResumeFromStageIndex: 1));
        }
        Assert.Equal(exhausted ? "Failed" : "Succeeded", result.Status);
        Assert.Equal(3, result.Steps[1].RetryCount);
        Assert.NotNull(result.FinishedAt);
        Assert.Equal(4, reviewer.States.Count);
        Assert.All(reviewer.States, state => Assert.Equal(payload, state));
        Assert.Equal(exhausted ? 1 : 2, dispatcher.DispatchedJobIds.Count);
        Assert.DoesNotContain(result.Steps, step => step.Status is "Running" or "Pending");
    }

    [Theory]
    [InlineData(0.5, 250)]
    [InlineData(0.99, 1)]
    [InlineData(0.99, 0)]
    public async Task Held_records_require_human_input_after_either_mapping_branch(double probability, int held)
    {
        var jobs = new InMemoryJobRepository();
        var accepted = MakeJob("accepted");
        var review = MakeJob("held");
        await jobs.AddAsync(accepted);
        await jobs.AddAsync(review);
        var chain = JobChain.Create(ProjectId, "review holds", null,
        [ChainStage.ForAction(new LoopChainAction(LoopChainAction.AgentType, "Check all records", "classification", agentProvider: LoopChainAction.JevAgent)),
         new ChainStage([accepted.Id], new ConditionGate("gte:classification.noul:0.9", [ChainStage.Of(review.Id)])),
         new ChainStage([], new ConditionGate("gt:summary.needs_review:0"), action:
             new LoopChainAction(LoopChainAction.HumanType, "Review every held record and record your decision", "human_review_decision"))], T0);
        var chains = new InMemoryJobChainRepository();
        await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher();
        foreach (var job in new[] { accepted, review })
            dispatcher.Results[job.Id] = Run(job.Id, "Succeeded", shardArtifacts: [$"{{\"summary\":{{\"needs_review\":{held}}}}}"]);
        var result = await RunHandler(chains, jobs, dispatcher, jev: new FakeOutcomeReviewer(probability))
            .HandleAsync(new RunJobChainCommand(chain.Id, "{\"records\":[1,2,3]}"));
        Assert.Equal(held > 0 ? "AwaitingInput" : "Succeeded", result.Status);
        Assert.Equal(probability >= 0.9 ? accepted.Id : review.Id, Assert.Single(dispatcher.DispatchedJobIds));
        if (held > 0)
        {
            Assert.Equal("humanInput", result.InputRequest!.Kind);
            Assert.Equal(2, Assert.Single(result.InputRequest.Steps).Index);
            Assert.Contains("records", result.FinalOutput);
        }
    }

    [Theory]
    [InlineData(true, 2, false, "Succeeded", true)]
    [InlineData(true, 1, false, "Failed", false)]
    [InlineData(false, 1, false, "Partial", true)]
    [InlineData(false, 2, true, "Failed", false)]
    public async Task Scoring_records_results_and_controls_downstream_execution(bool stop, double score, bool missing, string expected, bool downstream)
    {
        var jobs = new InMemoryJobRepository();
        var job = MakeJob("downstream"); await jobs.AddAsync(job);
        var action = new ScoringGateChainAction("evidence", "gate", [ScoringGateTests.Criterion("quality")], stopOnFailure: stop);
        var chain = JobChain.Create(ProjectId, "scoring", null, [ChainStage.ForAction(action), ChainStage.Of(job.Id)], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(chain);
        var dispatcher = new FakeRunDispatcher(); dispatcher.Results[job.Id] = Run(job.Id, "Succeeded");
        var reviewer = new ScoringReviewer(score);
        var handler = new RunJobChainHandler(chains, jobs, new InMemoryChainRunRepository(), new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(dispatcher), jev: reviewer);
        var run = await handler.HandleAsync(new RunJobChainCommand(chain.Id, InputPayload: missing ? "{\"gate\":{\"passed\":true}}" : "{\"evidence\":{\"tests\":\"passing\"}}"), default);
        Assert.Equal(expected, run.Status);
        Assert.Equal(downstream, run.Steps[1].Status == "Succeeded");
        var result = System.Text.Json.Nodes.JsonNode.Parse(run.FinalOutput!)!["gate"]!;
        Assert.Equal(!missing && score == 2, result["passed"]!.GetValue<bool>());
        Assert.NotEmpty(run.Steps[0].AgentActivity!);
        if (!missing)
        {
            Assert.NotNull(result["evidence_sha256"]);
            Assert.Equal("{\"tests\":\"passing\"}", reviewer.State);
            Assert.Equal(result.ToJsonString(), run.Steps[0].Output);
        }
    }
    [Theory]
    [InlineData(true, false, false, "Succeeded")]
    [InlineData(true, false, true, "Succeeded")]
    [InlineData(false, false, false, "Failed")]
    [InlineData(true, true, false, "Failed")]
    public async Task Data_query_passes_rows_to_job_and_fails_closed_on_denied_or_truncated(bool allowed, bool truncated, bool empty, string expected)
    {
        var jobs = new InMemoryJobRepository(); var job = MakeJob("consume data"); await jobs.AddAsync(job);
        var chain = JobChain.Create(ProjectId, "query", null, [ChainStage.ForAction(new QueryDataChainAction("SELECT id, title FROM listings LIMIT 10", "listings")), ChainStage.Of(job.Id)], T0);
        var chains = new InMemoryJobChainRepository(); await chains.AddAsync(chain);
        var reader = new QueryReader(truncated, empty); var dispatcher = new FakeRunDispatcher(); dispatcher.Results[job.Id] = Run(job.Id, "Succeeded");
        var handler = new RunJobChainHandler(chains, jobs, new InMemoryChainRunRepository(), new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(dispatcher), permissions: new FakePermissionService(allowed), dataReader: reader);
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id, InputPayload: "{\"keep\":42,\"listings\":{\"stale\":true}}"));
        Assert.Equal(expected, result.Status); Assert.Equal(allowed, reader.ProjectId.HasValue);
        if (expected == "Succeeded")
        {
            Assert.Equal(ProjectId, reader.ProjectId);
            var input = System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(dispatcher.Payloads)!)!;
            Assert.Equal(42, input["keep"]!.GetValue<int>());
            Assert.Equal(empty ? 0 : 1, input["listings"]!["row_count"]!.GetValue<int>());
            Assert.Equal(input["listings"]!.ToJsonString(), result.Steps[0].Output);
            if (!empty) { Assert.Equal("7", input["listings"]!["rows"]![0]!["id"]!.GetValue<string>()); Assert.Null(input["listings"]!["rows"]![0]!["title"]); }
        }
        else { Assert.Empty(dispatcher.Payloads); Assert.Null(System.Text.Json.Nodes.JsonNode.Parse(result.FinalOutput!)!["listings"]); }
    }
    private sealed class QueryReader(bool truncated, bool empty) : PlaceContext.Application.ProjectData.IProjectDataReader
    {
        public Guid? ProjectId;
        public Task<PlaceContext.Application.ProjectData.ProjectQueryResult> QueryAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            ProjectId = projectId; Assert.Contains("SELECT", sql);
            return Task.FromResult(new PlaceContext.Application.ProjectData.ProjectQueryResult(["id", "title"], empty ? [] : [new string?[] { "7", null }], 0, truncated));
        }
    }

    private sealed class ScoringReviewer(double score) : IOutcomeReviewer
    {
        public string? State;
        public Task<System.Text.Json.Nodes.JsonObject> ScoreAsync(IReadOnlyList<ScoreCriterion> criteria, string state, CancellationToken ct, Guid? projectId = null)
        { State = state; return Task.FromResult(ScoringGateTests.Response(("quality", score, .95))); }
        public Task<System.Text.Json.Nodes.JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null) => throw new NotSupportedException();
    }

    private sealed class ThrottledReviewer : IOutcomeReviewer
    {
        public Task<System.Text.Json.Nodes.JsonObject> ScoreAsync(IReadOnlyList<ScoreCriterion> criteria, string state, CancellationToken ct, Guid? projectId = null) => throw new NotSupportedException();
        public bool Succeed;
        public List<string?> States { get; } = [];
        public Task<System.Text.Json.Nodes.JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
        {
            States.Add(state);
            if (!Succeed) throw new OutcomeReviewThrottledException(States.Count % 2 == 0 ? 529 : 429, TimeSpan.FromSeconds(45));
            return Task.FromResult(new System.Text.Json.Nodes.JsonObject { ["noul"] = 0.99 });
        }
    }

    private sealed class ValidationQuestions(bool fail = false) : IChainQuestionGenerator
    {
        public List<string?> Outputs { get; } = [];
        public Task<string> GenerateAsync(LoopChainAction action, string? output, CancellationToken ct)
        {
            if (fail)
            {
                throw new InvalidOperationException("Model unavailable");
            }
            Outputs.Add(output);
            return Task.FromResult($"Validate: {action.Instructions}?");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeat_persists_attempts_and_rechecks_gates_after_resume(bool passOnSecondAttempt)
    {
        var job = MakeJob("quality");
        var protectedJob = MakeJob("protected delivery");
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);
        await jobs.AddAsync(protectedJob);
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "repeat", null,
            [new ChainStage([job.Id], new ConditionGate("eq:run:true")),
             ChainStage.ForAction(new RepeatFlowChainAction("eq:ready:true", 2)),
             ChainStage.Of(protectedJob.Id)], T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[job.Id] = Run(job.Id, "Succeeded", ["{\"ready\":false,\"run\":false}"]);
        dispatcher.Results[protectedJob.Id] = Run(protectedJob.Id, "Succeeded", ["{}"]);
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(dispatcher));
        var first = await handler.HandleAsync(new RunJobChainCommand(chain.Id, "{\"run\":true}"));
        Assert.Equal("Waiting", first.Status);
        Assert.DoesNotContain(protectedJob.Id, dispatcher.DispatchedJobIds);
        var stored = (await runs.GetByIdAsync(first.Id))!;
        Assert.Equal(1, stored.Steps[1].RetryCount);
        Assert.Equal(1, stored.ResumeStageIndex);
        if (passOnSecondAttempt) stored.SetCurrentPayload("{\"run\":true}");
        dispatcher.Results[job.Id] = Run(job.Id, "Succeeded", ["{\"ready\":true}"]);
        var second = await handler.HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: first.Id, ResumeFromStageIndex: 1));
        Assert.Equal(passOnSecondAttempt ? "Succeeded" : "Failed", second.Status);
        Assert.Equal(passOnSecondAttempt ? 3 : 1, dispatcher.DispatchedJobIds.Count);
        Assert.Equal(passOnSecondAttempt, dispatcher.DispatchedJobIds.Contains(protectedJob.Id));
        Assert.Equal(1, stored.Steps[1].RetryCount);
        Assert.Null(stored.ResumeAt);
    }

    [Fact]
    public async Task Agent_task_waits_with_exact_requirements_without_generating_a_review_question()
    {
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "requirements", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Define listing requirements", "requirements"))], T0);
        await chains.AddAsync(chain);
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), new InMemoryChainRunRepository(),
            new RecordingUnitOfWork(), new FakeClock(T0), new FakeJobRunner(new FakeRunDispatcher()));
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id));
        Assert.Equal("AwaitingInput", result.Status);
        Assert.Equal(LoopChainAction.TaskType, result.InputRequest!.Kind);
        Assert.Equal("Define listing requirements", result.InputRequest.Instructions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_agent_continues_with_structured_output_or_fails_without_pausing(bool fail)
    {
        var chains = new InMemoryJobChainRepository();
        var jobs = new InMemoryJobRepository();
        var next = MakeJob("after-agent");
        await jobs.AddAsync(next);
        var chain = JobChain.Create(ProjectId, "automatic", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result",
                agentProvider: LoopChainAction.AutomaticAgent, connectionId: Guid.NewGuid(), model: "test")),
             ChainStage.Of(next.Id)], T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var dispatcher = new FakeRunDispatcher();
        dispatcher.Results[next.Id] = Run(next.Id, "Succeeded");
        var agent = new TestWorkflowAgent(fail);
        var handler = new RunJobChainHandler(chains, jobs, runs, new RecordingUnitOfWork(), new FakeClock(T0),
            new FakeJobRunner(dispatcher), agentRunner: agent);
        var result = await handler.HandleAsync(new RunJobChainCommand(chain.Id, InputPayload: "{\"result\":{\"stale\":true}}"));
        Assert.Equal(fail ? "Failed" : "Succeeded", result.Status);
        Assert.Null(result.InputRequest);
        Assert.Equal(fail ? 0 : 1, dispatcher.DispatchedJobIds.Count);
        var stored = (await runs.GetByIdAsync(result.Id))!;
        Assert.Equal(fail ? ChainStepStatus.Failed : ChainStepStatus.Succeeded, stored.Steps[0].Status);
        if (!fail)
        {
            Assert.Contains("done", stored.FinalOutput);
            Assert.DoesNotContain("stale", stored.FinalOutput);
            Assert.Equal(stored.Steps[0].Output, result.Steps[0].Output);
            Assert.Contains("done", result.Steps[0].Output);
            Assert.DoesNotContain("stale", result.Steps[0].Output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Agent_activity_is_saved_while_running_and_does_not_override_cancellation(bool cancel)
    {
        var chains = new InMemoryJobChainRepository();
        var chain = JobChain.Create(ProjectId, "activity", null,
            [ChainStage.ForAction(new LoopChainAction(LoopChainAction.TaskType, "Build", "result",
                agentProvider: LoopChainAction.AutomaticAgent, connectionId: Guid.NewGuid(), model: "test"))], T0);
        await chains.AddAsync(chain);
        var runs = new InMemoryChainRunRepository();
        var agent = new ProgressWorkflowAgent();
        var handler = new RunJobChainHandler(chains, new InMemoryJobRepository(), runs, new RecordingUnitOfWork(),
            new FakeClock(T0), new FakeJobRunner(new FakeRunDispatcher()), agentRunner: agent);
        var id = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = handler.HandleAsync(new RunJobChainCommand(chain.Id, ChainRunId: id), timeout.Token);
        ChainRun? saved;
        do
        {
            await Task.Delay(20, timeout.Token);
            saved = await runs.GetByIdAsync(id);
        } while (saved?.Steps[0].AgentActivity?.Any(e => e.Kind == "model") != true);
        Assert.False(execution.IsCompleted);
        Assert.Equal(ChainRunStatus.Running, saved.Status);
        Assert.True(runs.SavedStepSnapshots.Count >= 3);
        if (cancel) saved.Complete(ChainRunStatus.Cancelled, saved.FinalOutput, T0);
        agent.Release.TrySetResult();
        var result = await execution;
        Assert.Equal(cancel ? "Cancelled" : "Succeeded", result.Status);
        Assert.Contains(result.Steps[0].AgentActivity!, e => e.Kind == "model");
        if (!cancel) Assert.Equal("completed", result.Steps[0].AgentActivity!.Last().Kind);
    }

    private sealed class ProgressWorkflowAgent : IWorkflowAgentRunner
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<System.Text.Json.Nodes.JsonNode> RunAsync(Guid projectId, Guid workflowId, Guid runId,
            int stepIndex, LoopChainAction action, string? payload, CancellationToken ct, Action<AgentActivity>? report = null)
        {
            report?.Invoke(new(T0, "model", "Waiting for model", Turn: 1));
            await Release.Task.WaitAsync(ct);
            report?.Invoke(new(T0, "completed", "Result ready"));
            return new System.Text.Json.Nodes.JsonObject { ["done"] = true };
        }
    }

    private sealed class TestWorkflowAgent(bool fail) : IWorkflowAgentRunner
    {
        public Task<System.Text.Json.Nodes.JsonNode> RunAsync(Guid projectId, Guid workflowId, Guid runId,
            int stepIndex, LoopChainAction action, string? payload, CancellationToken ct, Action<AgentActivity>? report = null) => fail
            ? throw new InvalidOperationException("Agent unavailable")
            : Task.FromResult<System.Text.Json.Nodes.JsonNode>(new System.Text.Json.Nodes.JsonObject { ["done"] = true });
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────

    private static JobRunDetailView Run(Guid jobId, string status,
        string[]? shardArtifacts = null, string? reduceArtifact = null,
        RunArtifactView[][]? shardNamedArtifacts = null)
    {
        var shards = (shardArtifacts ?? Array.Empty<string>())
            .Select((a, i) =>
            {
                var named = shardNamedArtifacts is { } && i < shardNamedArtifacts.Length
                    ? shardNamedArtifacts[i]
                    : Array.Empty<RunArtifactView>();
                return new ShardResultView(i, 0, "Succeeded", a, null, named);
            })
            .ToList();
        var reduce = reduceArtifact is null
            ? null
            : new ReduceResultView(0, true, reduceArtifact, null, Array.Empty<RunArtifactView>());
        return new JobRunDetailView(Guid.NewGuid(), jobId, ProjectId, status, T0, T0.AddSeconds(1),
            shards, reduce, new JobRunSnapshotView("image", "img", null, null, null, 1, shards.Count, false));
    }

    /// <summary>Dispatcher that answers RunJobCommand from a canned per-job result and records the
    /// payload each step received — the seam that lets chain logic be tested without a runner.
    /// Thread-safe (locked list mutations) and supports an optional artificial per-job delay so
    /// fan-out tests can force genuine overlap and observe how many branches were in flight at once
    /// — the same guarantee the real <c>Dispatcher</c> gives every dispatched command its own DI
    /// scope, so concurrent branches never contend on shared state.</summary>
    private sealed class FakeRunDispatcher : IDispatcher
    {
        private readonly object _gate = new();
        private int _active;

        public Dictionary<Guid, JobRunDetailView> Results { get; } = new();
        public Dictionary<Guid, TimeSpan> Delays { get; } = new();
        public Dictionary<Guid, Exception> Failures { get; } = new();
        public List<string?> Payloads { get; } = new();
        public List<Guid?> RunIds { get; } = new();
        public List<Guid> DispatchedJobIds { get; } = new();
        public int MaxObservedConcurrency
        {
            get; private set;
        }

        public async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            var run = (RunJobCommand)(object)command;
            lock (_gate)
            {
                Payloads.Add(run.InputPayload);
                RunIds.Add(run.RunId);
                DispatchedJobIds.Add(run.JobId);
            }

            var active = Interlocked.Increment(ref _active);
            lock (_gate)
            {
                if (active > MaxObservedConcurrency)
                {
                    MaxObservedConcurrency = active;
                }
            }
            try
            {
                if (Delays.TryGetValue(run.JobId, out var delay))
                {
                    await Task.Delay(delay, ct);
                }
                JobRunDetailView result;
                lock (_gate)
                {
                    if (Failures.TryGetValue(run.JobId, out var failure)) throw failure;
                    result = Results[run.JobId];
                }
                return (TResult)(object)result;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Thin IJobRunner wrapper around <see cref="FakeRunDispatcher"/> so chain tests keep
    /// using the same canned-result seam without real retry logic (test jobs have RetryCount=0).</summary>
    private sealed class FakeJobRunner : IJobRunner
    {
        private readonly FakeRunDispatcher _dispatcher;

        public FakeJobRunner(FakeRunDispatcher dispatcher) => _dispatcher = dispatcher;

        public Task<JobRunDetailView> RunAsync(Guid jobId, string? inputPayload = null, Guid? runId = null,
            Guid? replayOfRunId = null, CancellationToken ct = default)
            => _dispatcher.Send(new RunJobCommand(jobId, inputPayload, runId, replayOfRunId), ct);
    }

}
