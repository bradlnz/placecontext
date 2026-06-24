using System.ComponentModel;
using PlaceContext.Application;
using ModelContextProtocol.Server;

namespace PlaceContext.Host.Tools;

/// <summary>
/// MCP prompt surface: reusable, parameterized prompts an agent can invoke for common PlaceContext
/// scenarios. Each prompt is assembled from the project's <i>effective code requirements</i> (the
/// global document plus the project's own, defined in the portal) and its context document, so the
/// agent always works against the standards you set. Prompts are read-only — they return text to
/// steer the agent; tools (e.g. <c>record_change</c>) are what mutate state.
/// </summary>
[McpServerPromptType]
public sealed class PlaceContextPrompts
{
    [McpServerPrompt(Name = "review_code"), Description("Review the project's current changes against its code requirements (global + project) and context.")]
    public static async Task<string> ReviewCode(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, context) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        You are performing a **code review** of the working changes in this project.

        ## Code requirements (must be satisfied)
        {{requirements}}

        ## Project context
        {{context}}

        Review the current diff. For each finding give: file:line, the requirement or correctness issue,
        severity (blocker / major / minor), and a concrete fix. Call out any requirement above that is
        violated. If the change is clean against the requirements, say so explicitly.
        """;
    }

    [McpServerPrompt(Name = "create_skill"), Description("Guide creating a reusable skill/command for the project for a coding agent (Claude Code or Codex), in that agent's format and following the project's requirements.")]
    public static async Task<string> CreateSkill(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId,
        [Description("Short skill name, e.g. 'run-tests' or 'add-endpoint'")] string skillName,
        [Description("Target coding agent: 'claude' (Claude Code) or 'codex' (OpenAI Codex CLI). Defaults to claude.")] string agent = "claude",
        [Description("Optional one-line description of what the skill should do")] string? description = null)
    {
        var (header, requirements, context) = await GatherAsync(svc, projectId);
        var intent = string.IsNullOrWhiteSpace(description) ? "(infer from the project's conventions)" : description!;
        var spec = AgentSkillSpec(agent, skillName);
        return $$"""
        {{header}}

        Create a reusable **{{spec.AgentName}}** skill named `{{skillName}}` for this project.

        Intent: {{intent}}

        {{spec.Instructions}}

        Make the skill obey the project's code requirements, and reuse its established tooling/commands.

        ## Code requirements
        {{requirements}}

        ## Project context
        {{context}}

        Write the file at the path above; for a Claude Code skill you can also persist it with the `scaffold_skill` tool.
        """;
    }

    /// <summary>Per-agent skill format guidance — Claude Code skills vs. Codex prompts/AGENTS.md.</summary>
    private static (string AgentName, string Instructions) AgentSkillSpec(string agent, string skillName)
        => (agent ?? "claude").Trim().ToLowerInvariant() switch
        {
            "codex" or "openai" => ("Codex", $$"""
                Author it the way the OpenAI Codex CLI expects:
                - Write a reusable prompt to `.codex/prompts/{{skillName}}.md` (Codex loads project prompts from there).
                - Start with a one-line summary of when to use it, then concrete, CLI-runnable steps — the exact
                  commands, files, and checks — matching how this codebase already does things, not generic advice.
                - If the guidance should always apply (not just on demand), add it to the project's `AGENTS.md` instead.
                """),
            _ => ("Claude Code", $$"""
                Author it the way the Claude Code CLI expects a skill:
                - Write it to `.claude/skills/{{skillName}}/SKILL.md`.
                - Begin with YAML frontmatter: `name` (kebab-case) and a precise `description` that states *when*
                  to use the skill (the CLI uses this to decide when to trigger it).
                - Then concrete, CLI-runnable steps (the exact commands, files, and checks), not generic advice.
                """),
        };

    [McpServerPrompt(Name = "record_change_guidance"), Description("Walk through recording a change correctly through the git-backed ledger so it passes the process-trust gates.")]
    public static async Task<string> RecordChangeGuidance(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, _) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        Record the change you just made through PlaceContext's `record_change` tool. To keep agentic
        debt low, satisfy every trust gate:

        1. **Rationale** — explain *why*, not just what.
        2. **Tests** — report tests added/changed; a change with no test activity is flagged.
        3. **Touched files & nodes** — list exactly what you changed (these become the scoped commit).
        4. **Architecture review** — set `architectureReviewerRun` only if you actually ran it.
        5. **Live verification** — set `liveVerified` only if you ran the app and observed the behavior.

        The change must also uphold the project's code requirements:

        ## Code requirements
        {{requirements}}

        Then call `record_change` with an honest, specific `commitMessage`.
        """;
    }

    [McpServerPrompt(Name = "onboard"), Description("Load the project's context and code requirements to start a session well-grounded.")]
    public static async Task<string> Onboard(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, context) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        You are starting a working session on this project. Before writing any code, load what is known:

        ## Project context
        {{context}}

        ## Code requirements you must follow
        {{requirements}}

        Acknowledge the key constraints, then ask what to work on (or proceed with the stated task).
        Record what you learn with `add_context`, and route every change through `record_change`.
        """;
    }

    /// <summary>Fetches the project header, effective requirements, and context — with friendly fallbacks.</summary>
    private static async Task<(string Header, string Requirements, string Context)> GatherAsync(
        IPlaceContextService svc, Guid projectId)
    {
        var overview = await svc.GetProjectOverviewAsync(projectId);
        var reqs = await svc.GetEffectiveRequirementsAsync(projectId);
        var ctx = await svc.GetContextAsync(projectId);

        var header = $"# Project: {overview.Name}\nPath: {overview.Path}\nStatus: {overview.Status}";
        var requirements = reqs.IsEmpty ? "_No code requirements defined yet (global or project)._" : reqs.Markdown;
        var context = ctx.IsEmpty ? "_No context recorded yet._" : ctx.Markdown;
        return (header, requirements, context);
    }
}
