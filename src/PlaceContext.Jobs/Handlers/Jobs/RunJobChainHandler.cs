using System.Diagnostics;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Runs a chain as a staged pipeline of <see cref="ChainStage"/>s. A stage with a single job runs as
/// an ordinary sequential step, exactly as before; a stage with more than one job is a parallel
/// fan-out group — every branch is dispatched concurrently (bounded — see
/// <see cref="MaxConcurrentBranches"/>) and the stage is all-or-nothing: it only "succeeds" once
/// every branch reaches a non-Failed terminal state. If any branch in a stage ends Failed, the whole
/// chain is marked Failed and every later stage — including the join that would have followed a
/// fan-out group — is left Pending, which <see cref="ChainRun.Complete"/> turns into Skipped; the
/// join never runs. A Partial branch downgrades the chain to Partial but does not by itself halt it.
///
/// Each stage's primary output becomes the next stage's input payload, threaded exactly as a linear
/// chain always has: a single-job stage passes its one job's primary output through unchanged; a
/// fan-out stage combines every branch's primary output into one JSON array (the same "raw when
/// valid JSON, else JSON-encoded" convention already used to combine multiple map-shard artifacts)
/// so the join stage's job receives all of its upstream branches' outputs as its stdin input.
///
/// The run is persisted the moment it starts and saved on every step transition (pending → running →
/// outcome) — including concurrent transitions within a fan-out stage, serialized through a lock so
/// the portal always observes a consistent snapshot — so the portal can watch the pipeline progress
/// live and keep a history. A <c>job.chain</c> OTel span wraps the whole run (see
/// <see cref="PlaceContext.Application.Observability.JobTelemetry"/>); because each dispatched
/// <c>RunJobCommand</c> runs in the same async flow that started the span, its own <c>job.run</c>
/// span nests under it automatically — including for concurrent branches, since each awaited Task's
/// captured <see cref="Activity.Current"/> is independent of its siblings once forked.
/// </summary>
public sealed class RunJobChainHandler : ICommandHandler<RunJobChainCommand, ChainRunView>
{
    /// <summary>Caps how many branches of a fan-out stage dispatch at once — conservative, since each
    /// dispatched <c>RunJobCommand</c> opens its own DI scope/DbContext and may itself fan out map
    /// shards under its own concurrency limit.</summary>
    private const int MaxConcurrentBranches = 4;
    private const string FullFeasibilityReportChainName = "full-feasibility-report";

    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IChainRunRepository _runs;
    private readonly IJobsUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IJobRunner _jobRunner;
    private readonly IClientCommunicationSender? _communications;
    private readonly IPermissionService? _permissions;
    private readonly ICrmClientRepository? _crmClients;
    private readonly IReadOnlyList<IChainRunCompletionObserver> _completionObservers;
    private readonly IRunArtifactLinkRepository? _runArtifacts;
    private readonly IChainContextStore? _contextStore;
    private readonly ILogger<RunJobChainHandler>? _log;

    public RunJobChainHandler(IJobChainRepository chains, IJobRepository jobs, IChainRunRepository runs,
        IJobsUnitOfWork uow, IClock clock, IJobRunner jobRunner,
        // Optional so unit tests construct the handler unchanged; DI always supplies it.
        DataMappingIngestionService? dataMappings = null,
        IClientCommunicationSender? communications = null,
        IPermissionService? permissions = null,
        ICrmClientRepository? crmClients = null,
        IEnumerable<IChainRunCompletionObserver>? completionObservers = null,
        IRunArtifactLinkRepository? runArtifacts = null,
        IChainContextStore? contextStore = null,
        ILogger<RunJobChainHandler>? log = null)
    {
        _dataMappings = dataMappings;
        _chains = chains;
        _jobs = jobs;
        _runs = runs;
        _uow = uow;
        _clock = clock;
        _jobRunner = jobRunner;
        _communications = communications;
        _permissions = permissions;
        _crmClients = crmClients;
        _completionObservers = completionObservers?.ToList() ?? [];
        _runArtifacts = runArtifacts;
        _contextStore = contextStore;
        _log = log;
    }

    private readonly DataMappingIngestionService? _dataMappings;

    public async Task<ChainRunView> HandleAsync(RunJobChainCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Chain {command.ChainId} not found.");

        if (command.ResumeFromStageIndex is null
            && command.CrmClientId is { } clientId
            && string.Equals(chain.Name, FullFeasibilityReportChainName, StringComparison.Ordinal)
            && _runArtifacts is not null)
        {
            var existingReportRun = await TryGetExistingFeasibilityReportRunAsync(chain.Id, clientId, ct);
            if (existingReportRun is not null)
                return ChainRunMapper.ToView(existingReportRun);
        }

        // Snapshot every step's job up front — names go on the run so its history stands alone.
        var stepJobs = new List<Job?>(chain.ExecutionStepCount);
        var names = new List<string>(chain.ExecutionStepCount);
        foreach (var stage in chain.Stages)
        {
            if (stage.Action is { } action)
            {
                stepJobs.Add(null);
                names.Add(action.DisplayName);
                continue;
            }
            foreach (var jobId in stage.JobIds)
            {
                var job = await _jobs.GetByIdAsync(jobId, ct);
                stepJobs.Add(job);
                names.Add(job?.Name ?? "(deleted)");
            }
        }

        using var chainSpan = JobTelemetry.Activity.StartActivity("job.chain", ActivityKind.Internal);
        chainSpan?.SetTag("chain.id", chain.Id);
        chainSpan?.SetTag("chain.name", chain.Name);
        chainSpan?.SetTag("project.id", chain.ProjectId);
        JobTelemetry.ChainsStarted.Add(1, new("chain.id", chain.Id.ToString()), new("project.id", chain.ProjectId.ToString()));
        var chainStartedAt = _clock.UtcNow;

        ChainRun chainRun;
        var startStageIndex = command.ResumeFromStageIndex ?? 0;
        if (command.ResumeFromStageIndex is { } resumeStage)
        {
            if (command.ChainRunId is not { } existingRunId)
                throw new InvalidOperationException("A chain run id is required to resume a chain.");
            chainRun = await _runs.GetByIdAsync(existingRunId, ct)
                ?? throw new InvalidOperationException($"Chain run {existingRunId} not found.");
            if (chainRun.ChainId != chain.Id || chainRun.ResumeStageIndex != resumeStage)
                throw new InvalidOperationException("The scheduled chain continuation is no longer valid.");
            chainRun.Resume();
            await SaveProgressAsync(chainRun, ct);
        }
        else
        {
            chainRun = ChainRun.Start(chain, names, chainStartedAt, command.ChainRunId, command.CrmClientId);
            await _runs.AddAsync(chainRun, ct);
            await _uow.SaveChangesAsync(ct);
        }
        chainSpan?.SetTag("chain.run.id", chainRun.Id);

        // Serializes every write onto chainRun/_runs/_uow — dispatching itself (_dispatcher.Send) is
        // NOT held under this lock, so a fan-out stage's branches genuinely run concurrently; only the
        // "record the transition" moments are serialized, keeping every persisted snapshot consistent.
        using var progressLock = new SemaphoreSlim(1, 1);
        using var dispatchGate = new SemaphoreSlim(MaxConcurrentBranches, MaxConcurrentBranches);

        var status = ChainRunStatus.Succeeded;
        string? payload = command.ResumeFromStageIndex is not null
            ? await LoadContextAsync(chainRun, ct)
            : command.InputPayload; // first/resumed stage input
        await SaveContextAsync(chainRun, payload, ct);
        var flatIndex = chain.Stages.Take(startStageIndex).Sum(stage => stage.ExecutionCount);

        for (var stageIndex = startStageIndex; stageIndex < chain.Stages.Count; stageIndex++)
        {
            var stage = chain.Stages[stageIndex];

        // ── Evaluate gate before the stage ────────────────────────────────────────────────
            // The continuation is scheduled only after this gate has already evaluated. Skipping
            // it once on resume prevents a wait gate from scheduling itself forever.
            if (stage.Gate is { } gate
                && !(command.ResumeFromStageIndex == stageIndex && stageIndex == startStageIndex))
            {
                if (stage.Gate is ConditionGate && string.IsNullOrWhiteSpace(payload))
                    payload = await ResolvePreviousRunPayloadAsync(chain.Id, chainRun.CrmClientId, command.ResumeFromStageIndex, ct);

                var gateResult = gate.Evaluate(payload);
                if (gateResult.WaitDuration is { } wait)
                {
                    var overridesJson = command.StepPayloadOverrides is null
                        ? null
                        : JsonSerializer.Serialize(command.StepPayloadOverrides);
                    chainRun.Pause(stageIndex, payload, _clock.UtcNow.Add(wait), overridesJson);
                    await SaveProgressAsync(chainRun, ct);
                    chainSpan?.SetTag("status", ChainRunStatus.Waiting.ToString());
                    chainSpan?.SetTag("chain.resume_at", chainRun.ResumeAt);
                    return ChainRunMapper.ToView(chainRun);
                }
                if (!gateResult.Proceed)
                {
                    if (stage.Gate is ConditionGate { ElseBranch: { Count: > 0 } elseBranch })
                    {
                        // Condition false: skip this stage and execute the branch stages in-place.
                        for (var b = 0; b < stage.ExecutionCount; b++)
                        {
                            var idx = flatIndex + b;
                            chainRun.MarkStepFinished(idx, runId: null, ChainStepStatus.Skipped, _clock.UtcNow);
                        }
                        flatIndex += stage.ExecutionCount;
                        await SaveProgressAsync(chainRun, ct);

                        var branchResult = await ExecuteConditionalBranchAsync(
                            elseBranch, chainRun, payload, ct, progressLock, dispatchGate);
                        payload = branchResult.Payload;
                        status = branchResult.Status;
                        if (status == ChainRunStatus.Waiting)
                            return ChainRunMapper.ToView(chainRun);
                        if (status is ChainRunStatus.Failed or ChainRunStatus.Cancelled)
                            break;
                        continue;
                    }

                    // Condition gate false: skip this stage, keep payload unchanged.
                    for (var b = 0; b < stage.ExecutionCount; b++)
                    {
                        var idx = flatIndex + b;
                        chainRun.MarkStepFinished(idx, runId: null, ChainStepStatus.Skipped, _clock.UtcNow);
                    }
                    flatIndex += stage.ExecutionCount;
                    await SaveProgressAsync(chainRun, ct);
                    continue;
                }
            }

            if (stage.Action is SendEmailChainAction email)
            {
                var actionIndex = flatIndex++;
                chainRun.MarkStepRunning(actionIndex, runId: null, _clock.UtcNow);
                await SaveProgressAsync(chainRun, ct);
                try
                {
                    if (_communications is null || (chainRun.CrmClientId is null
                        && (_permissions is null || !await _permissions.HasAsync(Permission.EmailSend, ct))))
                        throw new UnauthorizedAccessException(
                            $"The '{Permission.EmailSend}' permission is required to send chain email.");
                    EnsureEmailReleaseAllowed(payload);
                    var customer = await LoadChainCustomerAsync(chainRun, ct);
                    var templatePayload = AddCustomerContext(payload, customer);
                    var recipient = customer?.Email
                        ?? RenderTemplate(email.Recipient, templatePayload);
                    var recipientName = customer?.Name
                        ?? RenderTemplate(email.RecipientName, templatePayload);
                    var subject = RenderTemplate(email.Subject, templatePayload);
                    var body = RenderTemplate(email.Body, templatePayload);
                    var attachments = ResolveEmailAttachments(email.AttachmentPath, templatePayload);
                    if (chainRun.CrmClientId is not null && string.IsNullOrWhiteSpace(recipient))
                        throw new InvalidOperationException("The CRM customer does not have an email address.");
                    _ = new MailAddress(recipient);
                    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                        throw new InvalidOperationException(
                            "Email subject and body must not be empty after payload substitution.");

                    var delivery = await _communications.SendEmailAsync(
                        recipient, recipientName, subject, body, ct, attachments);
                    chainRun.MarkStepFinished(actionIndex, runId: null,
                        ChainStepStatus.Succeeded, _clock.UtcNow,
                        delivery.Provider, delivery.ExternalId);
                    await SaveProgressAsync(chainRun, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    chainRun.MarkStepFinished(actionIndex, runId: null,
                        ChainStepStatus.Failed, _clock.UtcNow,
                        _communications?.EmailProvider, error: ShortError(ex.Message));
                    await SaveProgressAsync(chainRun, ct);
                    status = ChainRunStatus.Failed;
                    break;
                }
                continue;
            }

            if (stage.Action is SendSmsChainAction sms)
            {
                var actionIndex = flatIndex++;
                chainRun.MarkStepRunning(actionIndex, runId: null, _clock.UtcNow);
                await SaveProgressAsync(chainRun, ct);
                try
                {
                    if (_communications is null || (chainRun.CrmClientId is null
                        && (_permissions is null || !await _permissions.HasAsync(Permission.SmsSend, ct))))
                        throw new UnauthorizedAccessException(
                            $"The '{Permission.SmsSend}' permission is required to send chain SMS.");
                    EnsureReleaseAllowed(payload, "sms");
                    var customer = await LoadChainCustomerAsync(chainRun, ct);
                    var templatePayload = AddCustomerContext(payload, customer);
                    var recipient = customer?.Phone
                        ?? RenderTemplate(sms.Recipient, templatePayload);
                    var body = RenderTemplate(sms.Body, templatePayload);
                    var digits = new string(recipient.Where(char.IsDigit).ToArray());
                    if (!recipient.StartsWith('+') || digits.Length is < 8 or > 15)
                        throw new InvalidOperationException(
                            "SMS recipient must be an international number such as +61412345678.");
                    if (string.IsNullOrWhiteSpace(body))
                        throw new InvalidOperationException(
                            "SMS body must not be empty after payload substitution.");

                    var delivery = await _communications.SendSmsAsync(recipient, body, ct);
                    chainRun.MarkStepFinished(actionIndex, runId: null,
                        ChainStepStatus.Succeeded, _clock.UtcNow,
                        delivery.Provider, delivery.ExternalId);
                    await SaveProgressAsync(chainRun, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    chainRun.MarkStepFinished(actionIndex, runId: null,
                        ChainStepStatus.Failed, _clock.UtcNow,
                        _communications?.SmsProvider, error: ShortError(ex.Message));
                    await SaveProgressAsync(chainRun, ct);
                    status = ChainRunStatus.Failed;
                    break;
                }
                continue;
            }

            var stageStartFlat = flatIndex;
            var stagePayload = payload; // every branch of this stage reads the same upstream payload
            var branchOutputs = new string?[stage.JobIds.Count];
            var branchStatuses = new ChainStepStatus[stage.JobIds.Count];

            var branchTasks = Enumerable.Range(0, stage.JobIds.Count).Select(async branchIndex =>
            {
                var flat = stageStartFlat + branchIndex;
                var job = stepJobs[flat];

                if (job is null)
                {
                    branchStatuses[branchIndex] = ChainStepStatus.Failed;
                    await WithProgressLockAsync(progressLock, async () =>
                    {
                        chainRun.MarkStepFinished(flat, runId: null, ChainStepStatus.Failed, _clock.UtcNow);
                        await SaveProgressAsync(chainRun, ct);
                    });
                    return;
                }

                // Pre-allocate the branch's run id and record it before dispatching, so the live
                // pipeline (and the run-status watcher) can address it while it is still executing.
                var stepRunId = Guid.NewGuid();
                await WithProgressLockAsync(progressLock, async () =>
                {
                    chainRun.MarkStepRunning(flat, stepRunId, _clock.UtcNow);
                    await SaveProgressAsync(chainRun, ct);
                });

                var stepPayload = command.StepPayloadOverrides is { } overrides && overrides.TryGetValue(flat, out var args)
                    ? MergePayload(stagePayload, args)
                    : stagePayload;

                JobRunDetailView run;
                await dispatchGate.WaitAsync(ct);
                try
                {
                    run = await _jobRunner.RunAsync(stage.JobIds[branchIndex], stepPayload, stepRunId, ct: ct);
                }
                finally
                {
                    dispatchGate.Release();
                }

                var outcome = ParseStepOutcome(run.Status);
                branchStatuses[branchIndex] = outcome;
                branchOutputs[branchIndex] = PrimaryOutput(run);

                await WithProgressLockAsync(progressLock, async () =>
                {
                    chainRun.MarkStepFinished(flat, run.Id, outcome, _clock.UtcNow);
                    await SaveProgressAsync(chainRun, ct);
                });
            });

            await Task.WhenAll(branchTasks);
            flatIndex += stage.JobIds.Count;

            if (Array.IndexOf(branchStatuses, ChainStepStatus.Failed) >= 0)
            {
                // All-or-nothing: any branch failing halts the chain. Every later stage — including
                // the join that would have followed a fan-out group — stays Pending here and is
                // turned into Skipped by ChainRun.Complete below; it never runs.
                status = ChainRunStatus.Failed;
                break;
            }
            if (Array.IndexOf(branchStatuses, ChainStepStatus.Cancelled) >= 0)
            {
                // A cancelled branch also halts the chain, but marks it Cancelled not Failed.
                status = ChainRunStatus.Cancelled;
                break;
            }
            if (Array.IndexOf(branchStatuses, ChainStepStatus.Partial) >= 0)
                status = ChainRunStatus.Partial;

            payload = MergeContext(payload,
                stage.JobIds.Count == 1 ? branchOutputs[0] : CombineOutputs(branchOutputs));
            chainRun.SetCurrentPayload(payload);
            await SaveProgressAsync(chainRun, ct);
        }

        chainRun.Complete(status, payload, _clock.UtcNow);
        await SaveProgressAsync(chainRun, ct);
        await SaveContextAsync(chainRun, payload, ct);

        // Notify integrations only after the final chain state and context have been persisted.
        // Observer failures never rewrite a successfully completed chain as failed.
        foreach (var observer in _completionObservers)
        {
            try { await observer.OnCompletedAsync(chainRun, ct); }
            catch (Exception ex)
            {
                _log?.LogWarning(ex,
                    "Chain completion observer {ObserverType} failed for run {ChainRunId}.",
                    observer.GetType().Name, chainRun.Id);
            }
        }

        var finishedAt = _clock.UtcNow;
        chainSpan?.SetTag("status", status.ToString());
        chainSpan?.SetTag("chain.steps.json", BuildStepsTelemetryJson(chainRun));
        JobTelemetry.ChainsCompleted.Add(1, new("status", status.ToString()), new("chain.id", chain.Id.ToString()));
        JobTelemetry.ChainDuration.Record((finishedAt - chainStartedAt).TotalMilliseconds,
            new("status", status.ToString()), new("chain.id", chain.Id.ToString()));

        // The data map's chain edges: the pipeline's final output flows into its mapped tables.
        // Best-effort — ingestion must never fail the chain.
        if (_dataMappings is not null)
        {
            try { await _dataMappings.IngestChainOutputAsync(chain.Id, chainRun.Id, chain.ProjectId, payload, ct); }
            catch { /* isolated inside the service too */ }
        }

        return ChainRunMapper.ToView(chainRun);
    }

    private async Task<ChainRun?> TryGetExistingFeasibilityReportRunAsync(
        Guid chainId, Guid clientId, CancellationToken ct)
    {
        var runs = await _runs.ListForChainAsync(chainId, 20, ct);

        foreach (var run in runs)
        {
            if (run.CrmClientId != clientId)
                continue;
            if (run.Status is not (ChainRunStatus.Succeeded or ChainRunStatus.Partial))
                continue;

            foreach (var step in run.Steps)
            {
                if (step.RunId is not { } stepRunId)
                    continue;

                var artifacts = await _runArtifacts!.ListForRunAsync(stepRunId, ct);
                if (artifacts.Any(a => a.Kind == PostJobActionKind.HtmlReport))
                    return run;
            }
        }

        return null;
    }

    private async Task<(string? Payload, ChainRunStatus Status)> ExecuteConditionalBranchAsync(
        IReadOnlyList<ChainStage> stages, ChainRun chainRun, string? payload,
        CancellationToken ct, SemaphoreSlim progressLock, SemaphoreSlim dispatchGate)
    {
        var status = ChainRunStatus.Succeeded;
        var branchStageIndex = chainRun.StageCount;

        foreach (var stage in stages)
        {
            var result = await ExecuteConditionalStageAsync(
                stage, chainRun, branchStageIndex++, payload, ct, progressLock, dispatchGate);
            payload = result.Payload;
            status = result.Status;
            if (status is ChainRunStatus.Waiting or ChainRunStatus.Failed or ChainRunStatus.Cancelled)
                break;
        }

        return (payload, status);
    }

    private async Task<(string? Payload, ChainRunStatus Status)> ExecuteConditionalStageAsync(
        ChainStage stage, ChainRun chainRun, int stageIndex, string? payload, CancellationToken ct,
        SemaphoreSlim progressLock, SemaphoreSlim dispatchGate)
    {
        if (stage.Gate is { } gate)
        {
            if (gate is ConditionGate && string.IsNullOrWhiteSpace(payload))
                payload = await ResolvePreviousRunPayloadAsync(chainRun.ChainId, chainRun.CrmClientId, null, ct);

            var gateResult = gate.Evaluate(payload);
            if (gateResult.WaitDuration is { } wait)
            {
                var resumeAtStage = stageIndex >= chainRun.StageCount
                    ? Math.Max(chainRun.StageCount - 1, 0)
                    : stageIndex;
                chainRun.Pause(resumeAtStage, payload, _clock.UtcNow.Add(wait));
                await SaveProgressAsync(chainRun, ct);
                await SaveContextAsync(chainRun, payload, ct);
                return (payload, ChainRunStatus.Waiting);
            }
            if (!gateResult.Proceed)
            {
                if (gate is ConditionGate { ElseBranch: { Count: > 0 } elseBranch })
                    return await ExecuteConditionalBranchAsync(elseBranch, chainRun, payload, ct, progressLock, dispatchGate);

                return (payload, ChainRunStatus.Succeeded);
            }
        }

        if (stage.Action is SendEmailChainAction email)
        {
            var actionIndex = chainRun.AppendStep("send-email", stageIndex);
            chainRun.MarkStepRunning(actionIndex, runId: null, _clock.UtcNow);
            await SaveProgressAsync(chainRun, ct);

            try
            {
                if (_communications is null || (chainRun.CrmClientId is null
                    && (_permissions is null || !await _permissions.HasAsync(Permission.EmailSend, ct))))
                    throw new UnauthorizedAccessException(
                        $"The '{Permission.EmailSend}' permission is required to send chain email.");

                EnsureEmailReleaseAllowed(payload);
                var customer = await LoadChainCustomerAsync(chainRun, ct);
                var templatePayload = AddCustomerContext(payload, customer);
                var recipient = customer?.Email
                    ?? RenderTemplate(email.Recipient, templatePayload);
                var recipientName = customer?.Name
                    ?? RenderTemplate(email.RecipientName, templatePayload);
                var subject = RenderTemplate(email.Subject, templatePayload);
                var body = RenderTemplate(email.Body, templatePayload);
                var attachments = ResolveEmailAttachments(email.AttachmentPath, templatePayload);
                if (chainRun.CrmClientId is not null && string.IsNullOrWhiteSpace(recipient))
                    throw new InvalidOperationException("The CRM customer does not have an email address.");
                _ = new MailAddress(recipient);
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                    throw new InvalidOperationException(
                        "Email subject and body must not be empty after payload substitution.");

                var delivery = await _communications.SendEmailAsync(
                    recipient, recipientName, subject, body, ct, attachments);
                chainRun.MarkStepFinished(actionIndex, runId: null,
                    ChainStepStatus.Succeeded, _clock.UtcNow,
                    delivery.Provider, delivery.ExternalId);
                await SaveProgressAsync(chainRun, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                chainRun.MarkStepFinished(actionIndex, runId: null,
                    ChainStepStatus.Failed, _clock.UtcNow,
                    _communications?.EmailProvider, error: ShortError(ex.Message));
                await SaveProgressAsync(chainRun, ct);
                return (payload, ChainRunStatus.Failed);
            }

            return (payload, ChainRunStatus.Succeeded);
        }

        if (stage.Action is SendSmsChainAction sms)
        {
            var actionIndex = chainRun.AppendStep("send-sms", stageIndex);
            chainRun.MarkStepRunning(actionIndex, runId: null, _clock.UtcNow);
            await SaveProgressAsync(chainRun, ct);
            try
            {
                if (_communications is null || (chainRun.CrmClientId is null
                    && (_permissions is null || !await _permissions.HasAsync(Permission.SmsSend, ct))))
                    throw new UnauthorizedAccessException(
                        $"The '{Permission.SmsSend}' permission is required to send chain SMS.");
                EnsureReleaseAllowed(payload, "sms");
                var customer = await LoadChainCustomerAsync(chainRun, ct);
                var templatePayload = AddCustomerContext(payload, customer);
                var recipient = customer?.Phone
                    ?? RenderTemplate(sms.Recipient, templatePayload);
                var body = RenderTemplate(sms.Body, templatePayload);
                var digits = new string(recipient.Where(char.IsDigit).ToArray());
                if (!recipient.StartsWith('+') || digits.Length is < 8 or > 15)
                    throw new InvalidOperationException(
                        "SMS recipient must be an international number such as +61412345678.");
                if (string.IsNullOrWhiteSpace(body))
                    throw new InvalidOperationException(
                        "SMS body must not be empty after payload substitution.");

                var delivery = await _communications.SendSmsAsync(recipient, body, ct);
                chainRun.MarkStepFinished(actionIndex, runId: null,
                    ChainStepStatus.Succeeded, _clock.UtcNow,
                    delivery.Provider, delivery.ExternalId);
                await SaveProgressAsync(chainRun, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                chainRun.MarkStepFinished(actionIndex, runId: null,
                    ChainStepStatus.Failed, _clock.UtcNow,
                    _communications?.SmsProvider, error: ShortError(ex.Message));
                await SaveProgressAsync(chainRun, ct);
                return (payload, ChainRunStatus.Failed);
            }

            return (payload, ChainRunStatus.Succeeded);
        }

        var stagePayload = payload;
        var branchOutputs = new string?[stage.JobIds.Count];
        var branchStatuses = new ChainStepStatus[stage.JobIds.Count];
        var resolvedJobs = new List<Job?>(stage.JobIds.Count);
        foreach (var jobId in stage.JobIds)
            resolvedJobs.Add(await _jobs.GetByIdAsync(jobId, ct));
        var status = ChainRunStatus.Succeeded;

        var branchTasks = Enumerable.Range(0, stage.JobIds.Count).Select(async branchIndex =>
        {
            var job = resolvedJobs[branchIndex];
            int flat = -1;
            Guid stepRunId = Guid.Empty;
            await WithProgressLockAsync(progressLock, async () =>
            {
                flat = chainRun.AppendStep(job?.Id ?? Guid.Empty, job?.Name ?? "(deleted)",
                    stageIndex, branchIndex);
                stepRunId = Guid.NewGuid();
                chainRun.MarkStepRunning(flat, stepRunId, _clock.UtcNow);
                await SaveProgressAsync(chainRun, ct);
            });

            if (job is null)
            {
                branchStatuses[branchIndex] = ChainStepStatus.Failed;
                var error = $"The chain job '{stage.JobIds[branchIndex]}' does not exist.";
                await WithProgressLockAsync(progressLock, async () =>
                {
                    chainRun.MarkStepFinished(flat, runId: null, ChainStepStatus.Failed,
                        _clock.UtcNow, error: error);
                    await SaveProgressAsync(chainRun, ct);
                });
                return;
            }

            await dispatchGate.WaitAsync(ct);
            JobRunDetailView run;
            try
            {
                run = await _jobRunner.RunAsync(stage.JobIds[branchIndex], stagePayload, stepRunId, ct: ct);
            }
            finally
            {
                dispatchGate.Release();
            }
            var outcome = ParseStepOutcome(run.Status);
            branchStatuses[branchIndex] = outcome;
            branchOutputs[branchIndex] = PrimaryOutput(run);

            await WithProgressLockAsync(progressLock, async () =>
            {
                chainRun.MarkStepFinished(flat, run.Id, outcome, _clock.UtcNow);
                await SaveProgressAsync(chainRun, ct);
            });
        });

        await Task.WhenAll(branchTasks);

        if (Array.IndexOf(branchStatuses, ChainStepStatus.Failed) >= 0)
        {
            return (payload, ChainRunStatus.Failed);
        }
        if (Array.IndexOf(branchStatuses, ChainStepStatus.Cancelled) >= 0)
        {
            return (payload, ChainRunStatus.Cancelled);
        }
        if (Array.IndexOf(branchStatuses, ChainStepStatus.Partial) >= 0)
            status = ChainRunStatus.Partial;
        else
            status = ChainRunStatus.Succeeded;

        payload = MergeContext(payload,
            stage.JobIds.Count == 1 ? branchOutputs[0] : CombineOutputs(branchOutputs));
        chainRun.SetCurrentPayload(payload);
        await SaveContextAsync(chainRun, payload, ct);

        return (payload, status);
    }

    private async Task<string?> ResolvePreviousRunPayloadAsync(
        Guid chainId, Guid? crmClientId, int? resumeFromStageIndex, CancellationToken ct)
    {
        if (resumeFromStageIndex is not null) return null;
        var previousRuns = await _runs.ListForChainAsync(chainId, 20, ct);
        foreach (var previous in previousRuns)
        {
            if (previous.CrmClientId != crmClientId)
                continue;
            if (previous.Status is not (ChainRunStatus.Succeeded or ChainRunStatus.Partial))
                continue;
            return previous.FinalOutput;
        }

        return null;
    }

    private async Task<CrmClient?> LoadChainCustomerAsync(ChainRun run, CancellationToken ct)
    {
        if (run.CrmClientId is not { } clientId) return null;
        if (_crmClients is null)
            throw new InvalidOperationException("CRM customer lookup is unavailable.");
        return await _crmClients.GetByIdAsync(clientId, ct)
            ?? throw new InvalidOperationException($"CRM customer {clientId} no longer exists.");
    }

    internal static string? AddCustomerContext(string? payload, CrmClient? customer)
    {
        if (customer is null) return payload;
        var root = string.IsNullOrWhiteSpace(payload)
            ? new System.Text.Json.Nodes.JsonObject()
            : System.Text.Json.Nodes.JsonNode.Parse(payload) as System.Text.Json.Nodes.JsonObject
                ?? new System.Text.Json.Nodes.JsonObject
                {
                    ["previous"] = System.Text.Json.Nodes.JsonNode.Parse(payload),
                };
        root["customer"] = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = customer.Id,
            ["name"] = customer.Name,
            ["company"] = customer.Company,
            ["email"] = customer.Email,
            ["phone"] = customer.Phone,
        };
        return root.ToJsonString();
    }

    private static async Task WithProgressLockAsync(SemaphoreSlim gate, Func<Task> action)
    {
        await gate.WaitAsync();
        try { await action(); }
        finally { gate.Release(); }
    }

    // Fold collected step parameters over the chained input: two JSON objects merge shallowly
    // (parameter values win); anything else keeps the chained input under "previous" beside them.
    private static string MergePayload(string? chained, string args)
    {
        if (string.IsNullOrWhiteSpace(chained)) return args;
        try
        {
            var argsNode = System.Text.Json.Nodes.JsonNode.Parse(args) as System.Text.Json.Nodes.JsonObject
                ?? throw new JsonException();
            if (System.Text.Json.Nodes.JsonNode.Parse(chained) is System.Text.Json.Nodes.JsonObject prevObj)
            {
                foreach (var (k, v) in argsNode.ToList())
                    prevObj[k] = v?.DeepClone();
                return prevObj.ToJsonString();
            }
            argsNode["previous"] = System.Text.Json.Nodes.JsonNode.Parse(chained);
            return argsNode.ToJsonString();
        }
        catch
        {
            return args; // unparseable input — the collected parameters win
        }
    }

    private async Task SaveProgressAsync(ChainRun run, CancellationToken ct)
    {
        await _runs.UpdateAsync(run, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<string?> LoadContextAsync(ChainRun run, CancellationToken ct)
    {
        if (_contextStore is not null)
        {
            try
            {
                var cached = await _contextStore.GetAsync(run.Id, ct);
                if (cached is not null) return cached;
            }
            catch (Exception ex) { _log?.LogWarning(ex, "Could not read chain context {ChainRunId} from Redis; using checkpoint.", run.Id); }
        }
        return run.FinalOutput;
    }

    private async Task SaveContextAsync(ChainRun run, string? payload, CancellationToken ct)
    {
        if (_contextStore is null) return;
        try { await _contextStore.SetAsync(run.Id, payload, ct); }
        catch (Exception ex) { _log?.LogWarning(ex, "Could not write chain context {ChainRunId} to Redis; DB checkpoint remains authoritative.", run.Id); }
    }

    private static string? MergeContext(string? context, string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return context;
        try
        {
            var next = System.Text.Json.Nodes.JsonNode.Parse(output);
            if (next is System.Text.Json.Nodes.JsonObject nextObject)
            {
                if (System.Text.Json.Nodes.JsonNode.Parse(context ?? "null") is System.Text.Json.Nodes.JsonObject current)
                {
                    DeepMerge(current, nextObject);
                    return current.ToJsonString();
                }
                nextObject["previous"] = context is null ? null : System.Text.Json.Nodes.JsonNode.Parse(context);
                return nextObject.ToJsonString();
            }
            if (next is System.Text.Json.Nodes.JsonArray branches
                && System.Text.Json.Nodes.JsonNode.Parse(context ?? "null") is System.Text.Json.Nodes.JsonObject envelope)
            {
                envelope["branches"] = branches;
                return envelope.ToJsonString();
            }
            return next?.ToJsonString();
        }
        catch (JsonException) { return output; }
    }

    private static void DeepMerge(System.Text.Json.Nodes.JsonObject target, System.Text.Json.Nodes.JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is System.Text.Json.Nodes.JsonObject sourceObject
                && target[key] is System.Text.Json.Nodes.JsonObject targetObject)
                DeepMerge(targetObject, sourceObject);
            else
                target[key] = value?.DeepClone();
        }
    }

    private static ChainStepStatus ParseStepOutcome(string runStatus) => runStatus switch
    {
        "Succeeded" => ChainStepStatus.Succeeded,
        "Partial" => ChainStepStatus.Partial,
        "Cancelled" => ChainStepStatus.Cancelled,
        _ => ChainStepStatus.Failed,
    };

    /// <summary>The run's primary output, as the next stage's stdin payload: the reduce artifact when
    /// present (the final aggregate), a lone shard's artifact as-is, else a JSON array of the shard
    /// artifacts (raw values when they are JSON, JSON-encoded strings otherwise). When no stdout
    /// artifact was produced, non-binary named output files (<see cref="RunArtifactView"/>) are
    /// used as a fallback so downstream chain stages still receive the run's data.</summary>
    internal static string? PrimaryOutput(JobRunDetailView run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } reduce) return reduce;

        var artifacts = run.ShardResults
            .OrderBy(s => s.Index)
            .Where(s => !string.IsNullOrWhiteSpace(s.Artifact))
            .Select(s => s.Artifact!)
            .ToList();
        if (artifacts.Count > 0)
            return artifacts.Count == 1 ? artifacts[0] : CombineJsonValues(artifacts);

        var namedContents = run.ShardResults
            .OrderBy(s => s.Index)
            .SelectMany(s => s.Artifacts)
            .Where(a => !a.IsBinary && !string.IsNullOrWhiteSpace(a.Content))
            .Select(a => a.Content)
            .ToList();
        if (namedContents.Count == 0) return null;
        if (namedContents.Count == 1) return namedContents[0];
        return CombineJsonValues(namedContents);
    }

    /// <summary>The join's stdin input: every fan-out branch's primary output, in branch order,
    /// combined with the same convention <see cref="PrimaryOutput"/> uses for multiple shard
    /// artifacts — a raw JSON array whose elements are embedded as-is when they're already valid
    /// JSON, else JSON-encoded as a string. A branch with no output (e.g. it produced nothing)
    /// contributes a JSON <c>null</c>.</summary>
    internal static string? CombineOutputs(IReadOnlyList<string?> branchOutputs)
    {
        if (branchOutputs.Count == 0) return null;
        if (branchOutputs.Count == 1) return branchOutputs[0];
        return CombineJsonValues(branchOutputs.Select(o => o ?? "null"));
    }

    private static string CombineJsonValues(IEnumerable<string> values)
    {
        var parts = values.Select(v =>
        {
            try
            {
                using var doc = JsonDocument.Parse(v);
                return v; // already valid JSON — embed raw
            }
            catch
            {
                return JsonSerializer.Serialize(v); // plain text — embed as a JSON string
            }
        });
        return "[" + string.Join(",", parts) + "]";
    }

    private static readonly Regex TemplatePath = new(
        "\\{\\{\\s*([^{}]+?)\\s*\\}\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string RenderTemplate(string template, string? payload)
    {
        if (string.IsNullOrEmpty(template) || !TemplatePath.IsMatch(template)) return template;
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Message template needs a previous-stage payload.");
        using var document = JsonDocument.Parse(payload);
        return TemplatePath.Replace(template, match =>
        {
            if (!TryResolvePath(document.RootElement, match.Groups[1].Value, out var value))
                throw new InvalidOperationException(
                    $"Message template path '{match.Groups[1].Value}' was not found in the previous-stage payload.");
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        });
    }

    /// <summary>
    /// Resolves a provider-neutral attachment object (or array) from the previous stage JSON.
    /// Supported keys are <c>name</c>/<c>fileName</c>, <c>content</c>/<c>data</c> or
    /// <c>contentBase64</c>, and <c>contentType</c>/<c>mimeType</c>. Binary content must either use
    /// the explicit base64 key or set <c>isBinary</c>/<c>isBase64</c> to true. URLs are deliberately
    /// not fetched, avoiding an SSRF path from workload output into the portal network.
    /// </summary>
    internal static IReadOnlyList<ClientEmailAttachment> ResolveEmailAttachments(
        string attachmentPath,
        string? payload)
    {
        if (string.IsNullOrWhiteSpace(attachmentPath)) return Array.Empty<ClientEmailAttachment>();
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Email attachments need a previous-stage JSON payload.");

        var path = attachmentPath.Trim();
        var templateMatch = TemplatePath.Match(path);
        if (templateMatch.Success && templateMatch.Length == path.Length)
            path = templateMatch.Groups[1].Value;
        else if (TemplatePath.IsMatch(path))
            throw new InvalidOperationException("Attachment mapping must contain one JSON path only.");

        using var document = JsonDocument.Parse(payload);
        if (!TryResolvePath(document.RootElement, path, out var value))
            throw new InvalidOperationException(
                $"Email attachment path '{path}' was not found in the previous-stage payload.");

        var values = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.Clone()).ToList()
            : new List<JsonElement> { value.Clone() };
        if (values.Count == 0) return Array.Empty<ClientEmailAttachment>();
        if (values.Count > 20)
            throw new InvalidOperationException("An email action can attach at most 20 files.");

        var result = new List<ClientEmailAttachment>(values.Count);
        long totalBytes = 0;
        foreach (var (item, index) in values.Select((item, index) => (item, index)))
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Email attachment {index + 1} must be a JSON object with name and content fields.");

            var name = JsonString(item, "name", "fileName", "filename");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"Email attachment {index + 1} is missing its file name.");
            name = Path.GetFileName(name.Trim());
            if (name.Length == 0 || name.Length > 255)
                throw new InvalidOperationException($"Email attachment {index + 1} has an invalid file name.");

            var explicitBase64 = TryJsonString(item, out var content, "contentBase64", "base64");
            if (!explicitBase64 && !TryJsonString(item, out content, "content", "data"))
                throw new InvalidOperationException($"Email attachment '{name}' is missing its content.");
            var binary = explicitBase64 || JsonBool(item, "isBinary", "isBase64");
            byte[] bytes;
            if (binary)
            {
                try { bytes = Convert.FromBase64String(content!); }
                catch (FormatException)
                {
                    throw new InvalidOperationException($"Email attachment '{name}' is not valid base64.");
                }
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(content!);
            }

            totalBytes += bytes.LongLength;
            if (totalBytes > 10 * 1024 * 1024)
                throw new InvalidOperationException("Email attachments exceed the 10 MB total limit.");
            var contentType = JsonString(item, "contentType", "mimeType");
            if (string.IsNullOrWhiteSpace(contentType))
                contentType = binary ? "application/octet-stream" : "text/plain; charset=utf-8";
            result.Add(new ClientEmailAttachment(name, contentType, Convert.ToBase64String(bytes)));
        }
        return result;
    }

    private static string? JsonString(JsonElement item, params string[] names)
        => TryJsonString(item, out var value, names) ? value : null;

    private static bool TryJsonString(JsonElement item, out string? value, params string[] names)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            if (property.Value.ValueKind != JsonValueKind.String) break;
            value = property.Value.GetString();
            return value is not null;
        }
        value = null;
        return false;
    }

    private static bool JsonBool(JsonElement item, params string[] names)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            return property.Value.ValueKind == JsonValueKind.True;
        }
        return false;
    }

    internal static void EnsureEmailReleaseAllowed(string? payload)
        => EnsureReleaseAllowed(payload, "email");

    private static void EnsureReleaseAllowed(string? payload, string channel)
    {
        if (string.IsNullOrWhiteSpace(payload)) return;
        JsonDocument document;
        try { document = JsonDocument.Parse(payload); }
        catch (JsonException) { return; }
        using (document)
        {
            var flags = new List<bool>();
            CollectReleaseFlags(document.RootElement, flags, channel);
            if (flags.Count > 0 && flags.Any(flag => !flag))
                throw new InvalidOperationException(
                    $"Client {channel} delivery is blocked because the previous-stage release object does not permit {channel}/client release.");
        }
    }

    private static void CollectReleaseFlags(
        JsonElement element, ICollection<bool> flags, string channel)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalized = new string(property.Name
                    .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (normalized is "clientrelease" or "clientallowed" or "releaseclient"
                    || normalized == channel + "release"
                    || normalized == channel + "allowed"
                    || normalized == "release" + channel)
                {
                    if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        flags.Add(property.Value.GetBoolean());
                }
                CollectReleaseFlags(property.Value, flags, channel);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectReleaseFlags(item, flags, channel);
        }
    }

    private static bool TryResolvePath(JsonElement root, string rawPath, out JsonElement value)
    {
        value = root;
        var path = rawPath.Trim().TrimStart('$').TrimStart('.');
        if (path.Length == 0) return true;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty(segment, out var property))
            {
                value = property;
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, out var index)
                && index >= 0 && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }
            return false;
        }
        return true;
    }

    private static string ShortError(string value)
        => value.Length <= 500 ? value : value[..497] + "…";

    /// <summary>
    /// The chain's step summary, attached to the <c>job.chain</c> span as a single tag when the run
    /// finishes. There's no way to tag stage/branch position onto each dispatched <c>job.run</c>
    /// activity from here without reaching into <c>RunJobHandler</c> (out of scope for this change),
    /// so the chain — which already owns this structure via <see cref="ChainRun"/> — publishes it
    /// itself; the Infrastructure collector parses this tag back into
    /// <see cref="Ports.ChainRunStepTelemetry"/> when it captures the stopped <c>job.chain</c> activity.
    /// </summary>
    private static string BuildStepsTelemetryJson(ChainRun chainRun)
        => JsonSerializer.Serialize(chainRun.Steps.Select(s => new
        {
            stageIndex = s.StageIndex,
            branchIndex = s.BranchIndex,
            jobId = s.JobId,
            jobName = s.JobName,
            runId = s.RunId,
            status = s.Status.ToString(),
            durationMs = s.StartedAt is { } started && s.FinishedAt is { } finished
                ? (finished - started).TotalMilliseconds
                : (double?)null,
        }));
}
