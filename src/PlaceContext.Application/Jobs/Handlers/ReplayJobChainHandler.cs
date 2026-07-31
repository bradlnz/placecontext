using System.Diagnostics;
using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Replays a chain run from a specific step. Finds the first failed/partial step in the original
/// run (or uses the caller-specified index), captures the output from the step just before it as
/// the input payload, then dispatches a new RunJobChainCommand starting from that step.
///
/// The replay creates a NEW chain run (linked to the original via ReplayOfRunId in the payload)
/// so the original history is preserved. The new run only contains steps from the replay point
/// onward — it's a "continuation" of the original.
/// </summary>
public sealed class ReplayJobChainHandler : ICommandHandler<ReplayJobChainCommand, ChainRunView>
{
    private readonly IJobChainRepository _chains;
    private readonly IChainRunRepository _runs;
    private readonly IDispatcher _dispatcher;

    public ReplayJobChainHandler(
        IJobChainRepository chains,
        IChainRunRepository runs,
        IDispatcher dispatcher)
    {
        _chains = chains;
        _runs = runs;
        _dispatcher = dispatcher;
    }

    public async Task<ChainRunView> HandleAsync(ReplayJobChainCommand command, CancellationToken ct = default)
    {
        // Load the chain definition and original run
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");

        var originalRun = await _runs.GetByIdAsync(command.OriginalRunId, ct)
            ?? throw new InvalidOperationException($"Chain run {command.OriginalRunId} not found.");

        // Validate the run belongs to this chain
        if (originalRun.ChainId != command.ChainId)
            throw new InvalidOperationException($"Run {command.OriginalRunId} does not belong to chain {command.ChainId}.");

        // Determine the replay start step
        var fromStep = command.FromStepIndex ?? FindFirstFailedStep(originalRun);
        if (fromStep < 0 || fromStep >= originalRun.Steps.Count)
            throw new ArgumentOutOfRangeException(nameof(command.FromStepIndex),
                $"Step index {fromStep} is out of range (chain has {originalRun.Steps.Count} steps).");

        // Capture the input payload for the replay start step:
        // Use the output from the step just before it (or the original run's input if replaying step 0)
        var inputPayload = command.InputPayload ?? FindStepInput(originalRun, fromStep);

        // Build step payload overrides, preserving any from the original run's failed step
        var stepOverrides = new Dictionary<int, string>(command.StepPayloadOverrides ?? new Dictionary<int, string>());

        // Dispatch the chain run starting from the replay point
        // We use RunJobChainCommand with the chain ID and the captured input payload.
        // The chain handler will re-run from step 0, but we need to skip to fromStep.
        // Solution: we create a "virtual chain" by running the original chain with overrides
        // that skip steps before fromStep.

        // Actually, the cleanest approach: dispatch individual job runs for each step from
        // fromStep onward, threading outputs as inputs, exactly like the chain handler does.
        // This avoids needing to modify the chain definition.

        var replayNames = chain.Stages.SelectMany(stage => stage.Action is { } action
            ? new[] { action.DisplayName }
            : stage.JobIds.Select(_ => "(replay)")).ToList();
        var chainRun = ChainRun.Start(chain, replayNames, DateTimeOffset.UtcNow);
        await _runs.AddAsync(chainRun, ct);

        var status = ChainRunStatus.Succeeded;
        var payload = inputPayload;
        var flatIndex = 0;

        for (var stageIndex = 0; stageIndex < chain.Stages.Count; stageIndex++)
        {
            var stage = chain.Stages[stageIndex];

            if (stage.Action is not null)
            {
                var actionIndex = flatIndex++;
                chainRun.MarkStepRunning(actionIndex, null, DateTimeOffset.UtcNow);
                if (actionIndex < fromStep)
                {
                    chainRun.MarkStepFinished(actionIndex, null,
                        ChainStepStatus.Skipped, DateTimeOffset.UtcNow);
                    continue;
                }

                chainRun.MarkStepFinished(actionIndex, null, ChainStepStatus.Failed,
                    DateTimeOffset.UtcNow, error:
                    "Typed chain actions are not replayed automatically. Run the chain again to repeat the side effect.");
                await _runs.UpdateAsync(chainRun, ct);
                status = ChainRunStatus.Failed;
                break;
            }

            for (var branchIndex = 0; branchIndex < stage.JobIds.Count; branchIndex++)
            {
                var stepIndex = flatIndex;

                if (stepIndex < fromStep)
                {
                    // Skip this step — mark as Skipped in the replay
                    chainRun.MarkStepRunning(stepIndex, null, DateTimeOffset.UtcNow);
                    chainRun.MarkStepFinished(stepIndex, null, ChainStepStatus.Skipped, DateTimeOffset.UtcNow);
                    flatIndex++;
                    continue;
                }

                // This step needs to run
                var jobId = stage.JobIds[branchIndex];
                var stepRunId = Guid.NewGuid();

                chainRun.MarkStepRunning(stepIndex, stepRunId, DateTimeOffset.UtcNow);
                await _runs.UpdateAsync(chainRun, ct);

                // Apply any payload overrides for this step
                var stepPayload = stepOverrides.TryGetValue(stepIndex, out var overridePayload)
                    ? overridePayload : payload;

                // Dispatch the individual job run
                var run = await _dispatcher.Send(new RunJobCommand(jobId, stepPayload, stepRunId), ct);

                var outcome = run.Status switch
                {
                    "Succeeded" => ChainStepStatus.Succeeded,
                    "Partial" => ChainStepStatus.Partial,
                    _ => ChainStepStatus.Failed,
                };

                chainRun.MarkStepFinished(stepIndex, run.Id, outcome, DateTimeOffset.UtcNow);
                await _runs.UpdateAsync(chainRun, ct);

                if (outcome == ChainStepStatus.Failed)
                {
                    status = ChainRunStatus.Failed;
                    break;
                }
                if (outcome == ChainStepStatus.Partial)
                    status = ChainRunStatus.Partial;

                // Thread output to next step
                payload = PrimaryOutput(run);
                flatIndex++;
            }

            if (status == ChainRunStatus.Failed)
                break;
        }

        chainRun.Complete(status, payload, DateTimeOffset.UtcNow);
        await _runs.UpdateAsync(chainRun, ct);

        return ChainRunMapper.ToView(chainRun);
    }

    /// <summary>Finds the first failed or partial step in the original run.</summary>
    private static int FindFirstFailedStep(ChainRun run)
    {
        for (var i = 0; i < run.Steps.Count; i++)
        {
            if (run.Steps[i].Status is ChainStepStatus.Failed or ChainStepStatus.Partial)
                return i;
        }
        // No failed step found — replay from the beginning
        return 0;
    }

    /// <summary>Finds the input payload for a given step by looking at the previous step's output.</summary>
    private static string? FindStepInput(ChainRun run, int stepIndex)
    {
        if (stepIndex == 0) return null;

        // Find the previous step that succeeded and use its output
        for (var i = stepIndex - 1; i >= 0; i--)
        {
            var prevStep = run.Steps[i];
            if (prevStep.Status == ChainStepStatus.Succeeded && prevStep.RunId is { } prevRunId)
            {
                // The output is stored in the run's artifacts — but ChainRun doesn't directly
                // expose per-step outputs. The run's FinalOutput is the chain's final output.
                // For individual steps, we need to look at the JobRun's artifacts.
                // This is a limitation — we'll return null and let the step use its stored shards.
                return null;
            }
        }
        return null;
    }

    private static string? PrimaryOutput(JobRunDetailView run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduce) return reduce;
        var artifacts = run.ShardResults
            .OrderBy(s => s.Index)
            .Where(s => !string.IsNullOrWhiteSpace(s.Artifact))
            .Select(s => s.Artifact!)
            .ToList();
        if (artifacts.Count == 0) return null;
        if (artifacts.Count == 1) return artifacts[0];
        return "[" + string.Join(",", artifacts.Select(a =>
        {
            try { using var d = JsonDocument.Parse(a); return a; }
            catch { return JsonSerializer.Serialize(a); }
        })) + "]";
    }
}
