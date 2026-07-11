using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class OnboardHandler : ICommandHandler<OnboardCommand, OnboardResultView>
{
    private static readonly string[] DocCandidates = { "README.md", "README", "AGENTS.md", "CLAUDE.md" };
    private static readonly string[] Prompts = { "review_work", "create_skill", "record_activity_guidance", "onboard" };

    private readonly IProjectRepository _projects;
    private readonly RiskAssessmentService _risk;
    private readonly IActivityLogRepository _ledgers;
    private readonly IGitPort _git;
    private readonly IProjectContextRepository _contexts;
    private readonly IRequirementsRepository _requirements;
    private readonly IRepoFiles _files;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public OnboardHandler(
        IProjectRepository projects, RiskAssessmentService risk, IActivityLogRepository ledgers, IGitPort git,
        IProjectContextRepository contexts, IRequirementsRepository requirements, IRepoFiles files,
        IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _risk = risk;
        _ledgers = ledgers;
        _git = git;
        _contexts = contexts;
        _requirements = requirements;
        _files = files;
        _uow = uow;
        _clock = clock;
    }

    public async Task<OnboardResultView> HandleAsync(OnboardCommand command, CancellationToken ct = default)
    {
        var path = ProjectPath.From(command.Path);
        var now = _clock.UtcNow;

        // 1. Get-or-create the project, then assess its risk.
        var project = await _projects.GetByPathAsync(path, ct);
        if (project is null)
        {
            var name = string.IsNullOrWhiteSpace(command.Name)
                ? command.Path.TrimEnd('/').Split('/').LastOrDefault() ?? command.Path
                : command.Name;
            project = Project.Discover(path, ProjectName.From(name), now);
            project.Register(now);
            await _projects.AddAsync(project, ct);
            await _uow.SaveChangesAsync(ct); // persist first so risk collaborators can load it
        }
        await _risk.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        // 2. Backfill the activity log from git history (oldest first; skip already-recorded commits).
        var changesBackfilled = await BackfillAsync(project, path, command.BackfillLimit, now, ct);

        // 3. Seed context from the repo's docs, if context is empty.
        var contextSeeded = await SeedContextAsync(project, path, now, ct);

        // 4. Scaffold a local skill + agent for the target AI agent.
        var requirements = await EffectiveRequirementsAsync(project.Id, ct);
        var agent = (command.Agent ?? "claude").Trim().ToLowerInvariant();
        var name2 = project.Name.Value;
        var (skillRel, agentRel) = Paths(agent);

        var skills = new List<string> { await _files.WriteAsync(path, skillRel, SkillMarkdown(agent, name2, requirements), ct) };
        var agents = new List<string> { await _files.WriteAsync(path, agentRel, AgentMarkdown(agent, name2, requirements), ct) };

        return new OnboardResultView(
            ViewMapper.ToSummary(project), changesBackfilled, contextSeeded, skills, agents, Prompts);
    }

    private async Task<int> BackfillAsync(Project project, ProjectPath path, int limit, DateTimeOffset now, CancellationToken ct)
    {
        if (!_git.IsRepository(path)) return 0;

        var ledger = await _ledgers.GetForProjectAsync(project.Id, ct);
        var seen = ledger.Records.Where(r => r.Commit is not null).Select(r => r.Commit!.Value).ToHashSet();

        var added = 0;
        foreach (var c in (await _git.GetRecentCommitsAsync(path, limit, ct)).Reverse())
        {
            var sha = c.Sha.Trim().ToLowerInvariant();
            if (sha.Length is < 7 or > 40 || !seen.Add(sha)) continue;

            var record = ledger.Append(
                FirstLine(c.Message),
                Author.Human(string.IsNullOrWhiteSpace(c.AuthorName) ? "unknown" : c.AuthorName),
                Rationale.None, TestDelta.None, RiskDelta.None, ActivityVerification.None,
                c.Files, Array.Empty<GraphNodeId>(), (c.Date == default ? now : c.Date).ToUniversalTime());
            record.AttachCommit(CommitSha.From(sha));
            added++;
        }

        if (added > 0)
        {
            await _ledgers.SaveAsync(ledger, ct);
            await _uow.SaveChangesAsync(ct);
        }
        return added;
    }

    private async Task<bool> SeedContextAsync(Project project, ProjectPath path, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _contexts.GetForProjectAsync(project.Id, ct);
        if (existing is { IsEmpty: false }) return false;

        var doc = await _files.ReadFirstAsync(path, DocCandidates, ct);
        if (string.IsNullOrWhiteSpace(doc)) return false;

        var context = existing ?? ProjectContext.Start(project.Id, now);
        context.Replace(doc.Length > 8000 ? doc[..8000] : doc, now);
        await _contexts.SaveAsync(context, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> EffectiveRequirementsAsync(ProjectId projectId, CancellationToken ct)
    {
        var global = await _requirements.GetGlobalAsync(ct);
        var project = await _requirements.GetForProjectAsync(projectId, ct);
        var parts = new List<string>();
        if (global is { IsEmpty: false }) parts.Add(global.Markdown.Trim());
        if (project is { IsEmpty: false }) parts.Add(project.Markdown.Trim());
        return parts.Count == 0 ? "_None defined yet._" : string.Join("\n\n", parts);
    }

    private static (string Skill, string Agent) Paths(string agent) => agent switch
    {
        "codex" or "openai" => (".codex/prompts/placecontext.md", ".codex/prompts/reviewer.md"),
        _ => (".claude/skills/placecontext/SKILL.md", ".claude/agents/reviewer.md"),
    };

    private static string SkillMarkdown(string agent, string name, string requirements)
    {
        var body = $$"""
        # PlaceContext workflow — {{name}}

        ## At session start
        - Call `get_context` and `get_project_overview` to load what's known.

        ## Before every change (pre-action)
        - Confirm the change is in scope of the requirements below.
        - Note your current token usage so you can report the cost of this change afterward.

        ## Guardrails — every change must pass these
        - **Rationale** — know and state *why*, not just what.
        - **Checks** — add or adjust the relevant checks; a change with no verification activity is flagged.
        - **Review** — run a review on non-trivial work.
        - **Live verification** — exercise the work and observe the result before calling it done.

        ## After every change (post-action)
        - `record_activity` — rationale, touched items, check deltas, and only the guardrail flags you actually met.
        - `record_usage` — the input/output tokens spent on *this* change, so cost is tracked per change.
        - `add_context` / `add_decision` — capture anything durable you learned or decided.

        ## Requirements
        {{requirements}}
        """;

        return agent is "codex" or "openai"
            ? "Use at the start of work and around every change in this project.\n\n" + body
            : $$"""
              ---
              name: placecontext
              description: Use at the start of work in {{name}} and around every change — load context and route changes through PlaceContext.
              ---

              {{body}}
              """;
    }

    private static string AgentMarkdown(string agent, string name, string requirements)
    {
        var body = $$"""
        You review the current work in {{name}}. Check each change against the requirements below and
        report findings as `where — severity (blocker/major/minor) — issue — concrete fix`. If the change
        is clean against the requirements, say so explicitly.

        ## Requirements
        {{requirements}}
        """;

        return agent is "codex" or "openai"
            ? "Reviewer prompt for " + name + ".\n\n" + body
            : $$"""
              ---
              name: reviewer
              description: Reviews changes in {{name}} against the project's requirements. Use after completing a piece of work.
              tools: Read, Grep, Glob, Bash
              ---

              {{body}}
              """;
    }

    private static string FirstLine(string message)
    {
        var line = (message ?? string.Empty).Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return string.IsNullOrEmpty(line) ? "(no message)" : line;
    }
}
