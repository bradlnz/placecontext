using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Workflows;

/// <summary>
/// Read-only preflight for a saved or in-editor workflow. This checks available definition
/// metadata, not live credentials, dataset contents, permissions of future actors, or a
/// real dry run. It must never execute a job or create a release implicitly.
/// </summary>
public static class WorkflowReadiness
{
    public static WorkflowReadinessReport Evaluate(
        Guid projectId,
        IReadOnlyList<JobChainStageView> stages,
        IReadOnlyList<JobView> jobs)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(jobs);
        var findings = new List<WorkflowReadinessFinding>();
        var catalog = jobs.Where(job => job.ProjectId == projectId).ToDictionary(job => job.Id);
        var checkedJobs = new HashSet<Guid>();
        var stageCount = 0;
        void Walk(IReadOnlyList<JobChainStageView> stagesToCheck, int depth, bool humanGateSeen)
        {
            if (depth > 16 || stageCount + stagesToCheck.Count > 200)
            {
                findings.Add(new("error", "Workflow structure is too deep", "Reduce nested stages and check again."));
                return;
            }
            foreach (var stage in stagesToCheck)
            {
                stageCount++;
                if (stage.Jobs.Count == 0 && stage.Action is null)
                {
                    findings.Add(new("error", "Empty workflow step", "Every stage needs a job or executable action."));
                }
                foreach (var step in stage.Jobs)
                {
                    if (!catalog.TryGetValue(step.JobId, out var job))
                    {
                        findings.Add(new("error", "Unresolved job", "A workflow step points to a missing or inaccessible job."));
                        continue;
                    }
                    if (!checkedJobs.Add(job.Id))
                    {
                        continue;
                    }
                    if (job.MapSourceKind == "image" && string.IsNullOrWhiteSpace(job.MapImage)
                                            || job.MapSourceKind == "code"
                                                && string.IsNullOrWhiteSpace(job.MapSource)
                                                && job.MapFiles.Count == 0
                                            || job.MapSourceKind is not ("image" or "code"))
                    {
                        findings.Add(new("error", "Job has no executable source",
                                                $"Configure the workload source for {job.Name} before running."));
                    }
                    if (job.MapSourceKind == "code" && string.IsNullOrWhiteSpace(job.MapRuntimeId))
                    {
                        findings.Add(new("error", "Job runtime is missing",
                                                $"Choose a runtime for {job.Name}."));
                    }
                    if (job.Parameters.Any(p => p.Required))
                    {
                        findings.Add(new("info", "Job requests input",
                                                $"{job.Name} declares required parameters; the run may pause to collect them."));
                    }
                    if (job.McpConnectionIds.Count > 0)
                    {
                        findings.Add(new("info", "Connection permissions need checking",
                                                $"{job.Name} uses MCP connections. Confirm authorisation and availability before publishing."));
                    }
                    if (job.AllowNetworkEgress)
                    {
                        findings.Add(new("info", "Job uses network access",
                                                $"{job.Name} can access external services. Review source and credential scopes."));
                    }
                }

                if (stage.Action is LoopChainActionView loop
                    && loop.Kind == "humanInput")
                {
                    humanGateSeen = true;
                }
                if ((stage.Action is SendEmailChainActionView or SendSmsChainActionView)
                                    && !humanGateSeen)
                {
                    findings.Add(new("warning", "External action without earlier human step",
                                        "Check whether this notification or delivery action requires a human approval gate."));
                }
                if (stage.ElseBranch is { Count: > 0 } elseBranch)
                {
                    // A human gate in a conditional branch must not mark the opposite
                    // path as approved. Branches receive their own review state.
                    Walk(elseBranch, depth + 1, humanGateSeen);
                }
            }
        }

        void CheckStages()
        {
            if (stages.Count == 0)
            {
                findings.Add(new("error", "No workflow steps", "Add at least one job or action."));
                return;
            }
            Walk(stages, 0, false);
        }
        CheckStages();
        return new(checkedJobs.Count, stageCount, findings);
    }
}
