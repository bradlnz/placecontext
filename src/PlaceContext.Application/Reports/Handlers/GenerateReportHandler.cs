using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Hybrid report generator. Deterministically aggregates a project's stored data into the chosen
/// template's sections, builds a prioritised action plan, then — only when an
/// <see cref="ILlmGateway"/> is configured — polishes the assembled Markdown into prose.
/// Falls back to the deterministic Markdown when no LLM is available, so it always returns a report.
/// </summary>
public sealed class GenerateReportHandler : ICommandHandler<GenerateReportCommand, ReportView>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectContextRepository _contexts;
    private readonly IRequirementsRepository _requirements;
    private readonly IDecisionRepository _decisions;
    private readonly IActivityLogRepository _activity;
    private readonly IRiskAssessmentRepository _risk;
    private readonly IUsageRepository _usage;
    private readonly IReportTemplateRepository _templates;
    private readonly TokenCostCalculator _cost;
    private readonly ILlmGateway _llm;
    private readonly IClock _clock;

    public GenerateReportHandler(
        IProjectRepository projects, IProjectContextRepository contexts, IRequirementsRepository requirements,
        IDecisionRepository decisions, IActivityLogRepository activity,
        IRiskAssessmentRepository risk, IUsageRepository usage, IReportTemplateRepository templates,
        TokenCostCalculator cost, ILlmGateway llm, IClock clock)
    {
        _projects = projects;
        _contexts = contexts;
        _requirements = requirements;
        _decisions = decisions;
        _activity = activity;
        _risk = risk;
        _usage = usage;
        _templates = templates;
        _cost = cost;
        _llm = llm;
        _clock = clock;
    }

    public async Task<ReportView> HandleAsync(GenerateReportCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var template = await ResolveTemplateAsync(command.TemplateName, ct);

        // Aggregate everything once.
        var context = await _contexts.GetForProjectAsync(projectId, ct);
        var globalReqs = await _requirements.GetGlobalAsync(ct);
        var projectReqs = await _requirements.GetForProjectAsync(projectId, ct);
        var decisions = await _decisions.ListForProjectAsync(projectId, ct);
        var log = await _activity.GetForProjectAsync(projectId, ct);
        var latestRisk = await _risk.GetLatestAsync(projectId, ct);
        var usage = await _usage.ListForProjectAsync(projectId, ct);

        var actions = BuildActions(project, context, log, latestRisk);

        // Deterministic assembly.
        var now = _clock.UtcNow;
        var md = new StringBuilder();
        md.Append("# ").Append(template.Name).Append(" — ").AppendLine(project.Name.Value);
        md.Append("_Generated ").Append(now.ToString("yyyy-MM-dd HH:mm")).Append(" UTC_").AppendLine();

        foreach (var section in template.Sections)
        {
            md.AppendLine().Append("## ").AppendLine(section.Title);
            md.AppendLine(section.Source switch
            {
                ReportSourceKind.Overview => Overview(project, log, decisions),
                ReportSourceKind.Context => context is { IsEmpty: false } ? context.Markdown.Trim() : "_No context recorded yet._",
                ReportSourceKind.Requirements => Requirements(globalReqs, projectReqs),
                ReportSourceKind.Decisions => Decisions(decisions),
                ReportSourceKind.Activity => Activity(log),
                ReportSourceKind.Risk => Risk(latestRisk),
                ReportSourceKind.Usage => Usage(usage),
                ReportSourceKind.ActionPlan => ActionPlan(actions),
                _ => "_(no data)_"
            });
        }

        var assembled = md.ToString().TrimEnd();

        // Hybrid: polish only when a backend is configured.
        var markdown = assembled;
        var generatedByLlm = false;
        if (_llm.IsEnabled)
        {
            var system =
                $"You are a report writer. Rewrite the structured '{template.Name}' report below into a polished, " +
                "well-organised document for stakeholders. Preserve every fact and figure; do not invent anything. " +
                "Keep it concise and return Markdown only.";
            try
            {
                var polished = await _llm.GenerateAsync(system, assembled, ct);
                if (!string.IsNullOrWhiteSpace(polished)) { markdown = polished.Trim(); generatedByLlm = true; }
            }
            catch
            {
                // Generation is best-effort; fall back to the deterministic assembly.
            }
        }

        return new ReportView(
            project.Id.Value, project.Name.Value, template.Name, markdown,
            actions, generatedByLlm, now);
    }

    private async Task<ReportTemplate> ResolveTemplateAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BuiltInReportTemplates.ByName(BuiltInReportTemplates.OnboardingBriefName)!;

        return await _templates.GetByNameAsync(name, ct)
            ?? BuiltInReportTemplates.ByName(name)
            ?? throw new InvalidOperationException($"No report template named '{name}'.");
    }

    // ---- Action plan (deterministic; mirrors the focus/suggest-improvements signals) ----
    private static IReadOnlyList<ReportActionView> BuildActions(
        Project project, ProjectContext? context, ActivityLog log, RiskAssessment? risk)
    {
        var actions = new List<ReportActionView>();

        if (context is null || context.IsEmpty)
            actions.Add(new ReportActionView("medium", "Capture project context",
                "No context recorded — capture goals, conventions, and constraints with add_context so future sessions start informed."));

        if (!project.IsGraphified)
            actions.Add(new ReportActionView("low", "Build the knowledge graph",
                "Run rebuild_graph to map this project's structure from its logged activity."));

        var unverified = log.Records.Count(r => r.IsAgentAuthored && !r.Verification.LiveVerified);
        if (unverified > 0)
            actions.Add(new ReportActionView(unverified >= 3 ? "high" : "medium",
                $"Verify {unverified} agent change(s)",
                "Exercise the work and record live verification on record_activity."));

        if (risk is not null)
            foreach (var s in risk.Signals.Where(s => (int)s.Severity >= 2).OrderByDescending(s => (int)s.Severity).Take(3))
                actions.Add(new ReportActionView("medium", $"Risk signal: {s.Code}", s.Evidence));

        if (actions.Count == 0)
            actions.Add(new ReportActionView("low", "No issues detected",
                "No missing context, unverified changes, or risk signals from the logged activity."));

        return actions
            .OrderBy(a => a.Severity switch { "high" => 0, "medium" => 1, _ => 2 })
            .ToList();
    }

    // ---- Section renderers ----
    private static string Overview(Project p, ActivityLog log, IReadOnlyList<Decision> decisions)
    {
        var sb = new StringBuilder();
        sb.Append("- **Status:** ").AppendLine(p.Status.ToString());
        sb.Append("- **Technical risk:** ").AppendLine(p.TechnicalRisk is { } t ? $"{t.Value:0.0} ({t.Band})" : "not assessed");
        sb.Append("- **Process risk:** ").AppendLine(p.ProcessRisk is { } pr ? $"{pr.Value:0.0} ({pr.Band})" : "not assessed");
        sb.Append("- **Recorded changes:** ").AppendLine(log.Records.Count.ToString());
        sb.Append("- **Decisions:** ").Append(decisions.Count.ToString());
        return sb.ToString();
    }

    private static string Requirements(Requirements? global, Requirements? project)
    {
        var parts = new List<string>();
        if (global is { IsEmpty: false }) parts.Add("**Global**\n\n" + global.Markdown.Trim());
        if (project is { IsEmpty: false }) parts.Add("**Project**\n\n" + project.Markdown.Trim());
        return parts.Count == 0 ? "_No requirements defined yet._" : string.Join("\n\n", parts);
    }

    private static string Decisions(IReadOnlyList<Decision> decisions)
    {
        if (decisions.Count == 0) return "_No decisions recorded._";
        var sb = new StringBuilder();
        foreach (var d in decisions.OrderByDescending(d => d.DecidedAt).Take(10))
        {
            sb.Append("- **").Append(d.Question).Append("** → ").Append(d.Choice);
            if (d.Rationale.IsPresent) sb.Append(" — _").Append(d.Rationale.Value).Append('_');
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string Activity(ActivityLog log)
    {
        if (log.Records.Count == 0) return "_No activity recorded yet._";
        var sb = new StringBuilder();
        foreach (var r in log.Records.OrderByDescending(r => r.RecordedAt).Take(10))
            sb.Append("- ").Append(r.RecordedAt.ToString("yyyy-MM-dd")).Append(" · ")
              .Append(r.Author.Kind).Append(" · ").AppendLine(string.IsNullOrWhiteSpace(r.Summary) ? "(no summary)" : r.Summary);
        return sb.ToString().TrimEnd();
    }

    private static string Risk(RiskAssessment? a)
    {
        if (a is null) return "_No risk assessment yet._";
        var sb = new StringBuilder();
        sb.Append("- **Technical:** ").Append(a.Technical.Value.ToString("0.0")).Append(" (").Append(a.Technical.Band).AppendLine(")");
        sb.Append("- **Process:** ").Append(a.Process.Value.ToString("0.0")).Append(" (").Append(a.Process.Band).AppendLine(")");
        foreach (var s in a.Signals.OrderByDescending(s => (int)s.Severity).Take(5))
            sb.Append("- ").Append(s.Code).Append(" — ").AppendLine(s.Evidence);
        return sb.ToString().TrimEnd();
    }

    private string Usage(IReadOnlyList<UsageRecord> usage)
    {
        if (usage.Count == 0) return "_No usage recorded yet._";
        var totalIn = usage.Sum(u => u.Usage.InputTokens);
        var totalOut = usage.Sum(u => u.Usage.OutputTokens);
        var cost = usage.Sum(u => _cost.CostUsd(u.Usage));
        return $"- **Entries:** {usage.Count}\n- **Tokens:** {totalIn:N0} in / {totalOut:N0} out\n- **Cost:** ${cost:N2}";
    }

    private static string ActionPlan(IReadOnlyList<ReportActionView> actions)
    {
        var sb = new StringBuilder();
        foreach (var a in actions)
            sb.Append("- **[").Append(a.Severity).Append("] ").Append(a.Title).Append("** — ").AppendLine(a.Detail);
        return sb.ToString().TrimEnd();
    }
}
