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
    private static readonly string[] Prompts = { "review_code", "create_skill", "record_change_guidance", "onboard" };

    private readonly IProjectRepository _projects;
    private readonly DebtAssessmentService _debt;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly IGitPort _git;
    private readonly IProjectContextRepository _contexts;
    private readonly ICodeRequirementsRepository _requirements;
    private readonly IRepoFiles _files;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public OnboardHandler(
        IProjectRepository projects, DebtAssessmentService debt, IChangeLedgerRepository ledgers, IGitPort git,
        IProjectContextRepository contexts, ICodeRequirementsRepository requirements, IRepoFiles files,
        IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _debt = debt;
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
        var path = RepoPath.From(command.Path);
        var now = _clock.UtcNow;

        // 1. Get-or-create the project, then assess its debt.
        var project = await _projects.GetByPathAsync(path, ct);
        if (project is null)
        {
            var name = string.IsNullOrWhiteSpace(command.Name)
                ? command.Path.TrimEnd('/').Split('/').LastOrDefault() ?? command.Path
                : command.Name;
            project = Project.Discover(path, ProjectName.From(name), now);
            project.Register(now);
            await _projects.AddAsync(project, ct);
            await _uow.SaveChangesAsync(ct); // persist first so debt collaborators can load it
        }
        await _debt.AssessAsync(project, ct);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);

        // 2. Backfill the change ledger from git history (oldest first; skip already-recorded commits).
        var changesBackfilled = await BackfillAsync(project, path, command.BackfillLimit, now, ct);

        // 3. Seed context from the repo's docs, if context is empty.
        var contextSeeded = await SeedContextAsync(project, path, now, ct);

        // 4. Scaffold a local skill + agent for the target coding agent.
        var requirements = await EffectiveRequirementsAsync(project.Id, ct);
        var agent = (command.Agent ?? "claude").Trim().ToLowerInvariant();
        var name2 = project.Name.Value;
        var (skillRel, agentRel) = Paths(agent);

        var skills = new List<string> { await _files.WriteAsync(path, skillRel, SkillMarkdown(agent, name2, requirements), ct) };
        var agents = new List<string> { await _files.WriteAsync(path, agentRel, AgentMarkdown(agent, name2, requirements), ct) };

        return new OnboardResultView(
            ViewMapper.ToSummary(project), changesBackfilled, contextSeeded, skills, agents, Prompts);
    }

    private async Task<int> BackfillAsync(Project project, RepoPath path, int limit, DateTimeOffset now, CancellationToken ct)
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
                Rationale.None, TestDelta.None, DebtDelta.None, ChangeVerification.None,
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

    private async Task<bool> SeedContextAsync(Project project, RepoPath path, DateTimeOffset now, CancellationToken ct)
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
        "codex" or "openai" => (".codex/prompts/placecontext.md", ".codex/prompts/code-reviewer.md"),
        _ => (".claude/skills/placecontext/SKILL.md", ".claude/agents/code-reviewer.md"),
    };

    private static string SkillMarkdown(string agent, string name, string requirements)
    {
        var body = $$"""
        # PlaceContext workflow — {{name}}

        ## At session start
        - Call `get_context` and `get_project_overview` to load what's known.
        - Pull the next task with `next_work_item`; if none, ask what to work on.

        ## Before every change (pre-action)
        - Confirm the change is in scope of the claimed work item and the code requirements below.
        - Note your current token usage so you can report the cost of this change afterward.

        ## Guardrails — every change must pass these
        - **Rationale** — know and state *why*, not just what.
        - **Tests** — add or adjust tests; a change with no test activity is flagged.
        - **Architecture review** — run the reviewer on non-trivial slices.
        - **Live verification** — run the app and observe the behavior before calling it done.

        ## After every change (post-action)
        - `record_change` — rationale, touched files, test deltas, and only the guardrail flags you actually met.
        - `record_usage` — the input/output tokens spent on *this* change, so cost is tracked per change.
        - `complete_work_item` — mark the work item done.
        - `add_context` / `add_decision` — capture anything durable you learned or decided.

        ## Code requirements
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
        You review the working changes in {{name}}. Check each change against the code requirements below and
        report findings as `file:line — severity (blocker/major/minor) — issue — concrete fix`. If the change
        is clean against the requirements, say so explicitly.

        ## Code requirements
        {{requirements}}
        """;

        return agent is "codex" or "openai"
            ? "Code reviewer prompt for " + name + ".\n\n" + body
            : $$"""
              ---
              name: code-reviewer
              description: Reviews changes in {{name}} against the project's code requirements. Use after implementing a feature or fix.
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
