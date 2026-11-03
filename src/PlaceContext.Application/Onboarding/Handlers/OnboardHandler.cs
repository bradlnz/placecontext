using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Projects;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class OnboardHandler : ICommandHandler<OnboardCommand, OnboardResultView>
{
    private static readonly string[] Prompts = { "review_work", "create_skill", "record_activity_guidance", "onboard" };

    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IGitPort _git;
    private readonly IRequirementsRepository _requirements;
    private readonly IRepoFiles _files;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public OnboardHandler(
        IProjectRepository projects, IActivityLogRepository ledgers, IGitPort git,
        IRequirementsRepository requirements, IRepoFiles files,
        IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _ledgers = ledgers;
        _git = git;
        _requirements = requirements;
        _files = files;
        _uow = uow;
        _clock = clock;
    }

    public async Task<OnboardResultView> HandleAsync(OnboardCommand command, CancellationToken ct = default)
    {
        var path = ProjectPath.From(command.Path);
        var now = _clock.UtcNow;

        // 1. Get-or-create the project.
        var project = await _projects.GetByPathAsync(path, ct);
        if (project is null)
        {
            var name = string.IsNullOrWhiteSpace(command.Name)
                ? command.Path.TrimEnd('/').Split('/').LastOrDefault() ?? command.Path
                : command.Name;
            project = Project.Discover(path, ProjectName.From(name), now);
            project.Register(now);
            await _projects.AddAsync(project, ct);
            await _uow.SaveChangesAsync(ct);
        }

        // 2. Backfill the activity log from git history (oldest first; skip already-recorded commits).
        var changesBackfilled = await BackfillAsync(project, path, command.BackfillLimit, now, ct);

        // 3. Scaffold a local skill + agent for the target AI agent.
        var requirements = await EffectiveRequirementsAsync(project.Id, ct);
        var agent = (command.Agent ?? "claude").Trim().ToLowerInvariant();
        var name2 = project.Name.Value;
        var (skillRel, agentRel) = Paths(agent);

        var skills = new List<string> { await _files.WriteAsync(path, skillRel, SkillMarkdown(agent, name2, requirements), ct) };
        var agents = new List<string> { await _files.WriteAsync(path, agentRel, AgentMarkdown(agent, name2, requirements), ct) };

        return new OnboardResultView(
            ViewMapper.ToSummary(project), changesBackfilled, skills, agents, Prompts);
    }

    private async Task<int> BackfillAsync(Project project, ProjectPath path, int limit, DateTimeOffset now, CancellationToken ct)
    {
        if (!_git.IsRepository(path))
        {
            return 0;
        }
        var ledger = await _ledgers.GetForProjectAsync(project.Id, ct);
        var seen = ledger.Records.Where(r => r.Commit is not null).Select(r => r.Commit!.Value).ToHashSet();

        var added = 0;
        foreach (var c in (await _git.GetRecentCommitsAsync(path, limit, ct)).Reverse())
        {
            var sha = c.Sha.Trim().ToLowerInvariant();
            if (sha.Length is < 7 or > 40 || !seen.Add(sha))
            {
                continue;
            }
            var record = ledger.Append(
                FirstLine(c.Message),
                Author.Human(string.IsNullOrWhiteSpace(c.AuthorName) ? "unknown" : c.AuthorName),
                Rationale.None, TestDelta.None, ActivityVerification.None,
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

    private async Task<string> EffectiveRequirementsAsync(ProjectId projectId, CancellationToken ct)
    {
        var global = await _requirements.GetGlobalAsync(ct);
        var project = await _requirements.GetForProjectAsync(projectId, ct);
        var parts = new List<string>();
        if (global is { IsEmpty: false })
        {
            parts.Add(global.Markdown.Trim());
        }
        if (project is { IsEmpty: false })
        {
            parts.Add(project.Markdown.Trim());
        }
        return parts.Count == 0 ? "_None defined yet._" : string.Join("\n\n", parts);
    }

    private static (string Skill, string Agent) Paths(string agent) => agent switch
    {
        "codex" or "openai" => (".codex/prompts/placecontext.md", ".codex/prompts/reviewer.md"),
        _ => (".claude/skills/placecontext/SKILL.md", ".claude/agents/reviewer.md"),
    };

    private static string SkillMarkdown(string agent, string name, string requirements)
    {
        var body = PromptText.Render("Onboarding.Prompts.WorkflowSkill", ("name", name), ("requirements", requirements));

        return agent is "codex" or "openai"
            ? PromptText.Render("Onboarding.Prompts.WorkflowCommand", ("body", body))
            : PromptText.Render("Onboarding.Prompts.SkillFrontmatter", ("name", name), ("body", body));
    }

    private static string AgentMarkdown(string agent, string name, string requirements)
    {
        var body = PromptText.Render("Onboarding.Prompts.CodeReview", ("name", name), ("requirements", requirements));

        return agent is "codex" or "openai"
            ? PromptText.Render("Onboarding.Prompts.ReviewCommand", ("name", name), ("body", body))
            : PromptText.Render("Onboarding.Prompts.ReviewerFrontmatter", ("name", name), ("body", body));
    }

    private static string FirstLine(string message)
    {
        var line = (message ?? string.Empty).Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return string.IsNullOrEmpty(line) ? "(no message)" : line;
    }
}
