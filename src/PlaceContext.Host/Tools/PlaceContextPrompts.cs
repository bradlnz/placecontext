using System.ComponentModel;
using PlaceContext.Application;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace PlaceContext.Host.Tools;

/// <summary>
/// MCP prompt surface: reusable, parameterized prompts an agent can invoke for common PlaceContext
/// scenarios. Each prompt is assembled from the project's <i>effective requirements</i> (the
/// global document plus the project's own, defined in the portal) and its context document, so the
/// agent always works against the standards you set. Prompts are read-only — they return text to
/// steer the agent; tools (e.g. <c>record_activity</c>) are what mutate state.
/// </summary>
[McpServerPromptType]
public sealed class PlaceContextPrompts
{
    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "review_work"), Description("Review the project's current work against its requirements (global + project) and context.")]
    public static async Task<string> ReviewWork(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, context) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        You are performing a **review** of the current work in this project.

        ## Requirements (must be satisfied)
        {{requirements}}

        ## Project context
        {{context}}

        Review the current work. For each finding give: where it occurs, the requirement or correctness
        issue, severity (blocker / major / minor), and a concrete fix. Call out any requirement above that
        is violated. If the work is clean against the requirements, say so explicitly.
        """;
    }

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "create_skill"), Description("Guide creating a reusable skill/command for the project for an AI agent (Claude Code or Codex), in that agent's format and following the project's requirements.")]
    public static async Task<string> CreateSkill(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId,
        [Description("Short skill name, e.g. 'run-tests' or 'add-endpoint'")] string skillName,
        [Description("Target AI agent: 'claude' (Claude Code) or 'codex' (OpenAI Codex CLI). Defaults to claude.")] string agent = "claude",
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

        Make the skill obey the project's requirements, reuse its established tooling/commands, and bake
        in the PlaceContext workflow:
        - **Pre-action**: load context (`get_context`), pull the next task (`next_work_item`), note starting tokens.
        - **Guardrails** every change must pass: rationale, checks, review, live verification.
        - **Post-action**: `record_activity` (rationale/items/check deltas/guardrail flags), `record_usage`
          (tokens spent on that change — cost per change), and `complete_work_item`.

        ## Requirements
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
                  commands, files, and checks — matching how this project already does things, not generic advice.
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

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "record_activity_guidance"), Description("Walk through recording a change correctly into the activity log so it passes the process-trust gates.")]
    public static async Task<string> RecordActivityGuidance(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, _) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        Record the change you just made through PlaceContext's `record_activity` tool. To keep process
        risk low, satisfy every trust gate:

        1. **Rationale** — explain *why*, not just what.
        2. **Checks** — report checks added/changed; a change with no verification activity is flagged.
        3. **Touched items & nodes** — list exactly what you changed (these are the scope of the record).
        4. **Review** — set `architectureReviewerRun` only if you actually ran a review.
        5. **Live verification** — set `liveVerified` only if you exercised the work and observed the result.

        The change must also uphold the project's requirements:

        ## Requirements
        {{requirements}}

        Then call `record_activity` with an honest, specific `commitMessage`.
        """;
    }

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "onboard"), Description("Load the project's context and requirements to start a session well-grounded.")]
    public static async Task<string> Onboard(
        IPlaceContextService svc,
        [Description("The project's GUID id")] Guid projectId)
    {
        var (header, requirements, context) = await GatherAsync(svc, projectId);
        return $$"""
        {{header}}

        You are starting a working session on this project. Before starting work, load what is known:

        ## Project context
        {{context}}

        ## Requirements you must follow
        {{requirements}}

        Acknowledge the key constraints, then ask what to work on (or proceed with the stated task).
        Record what you learn with `add_context`, and route every change through `record_activity`.
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
        var requirements = reqs.IsEmpty ? "_No requirements defined yet (global or project)._" : reqs.Markdown;
        var context = ctx.IsEmpty ? "_No context recorded yet._" : ctx.Markdown;
        return (header, requirements, context);
    }
}
