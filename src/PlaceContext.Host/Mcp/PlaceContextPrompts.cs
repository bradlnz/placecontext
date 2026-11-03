using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using PlaceContext.Application.Shared;
using PlaceContext.Application;

namespace PlaceContext.Host.Tools;

/// <summary>
/// MCP prompt surface: reusable, parameterized prompts an agent can invoke for common PlaceContext
/// scenarios. Each prompt is assembled from the project's <i>effective requirements</i> (the
/// global document plus the project's own, defined in the portal), so the
/// agent always works against the standards you set. Prompts are read-only — they return text to
/// steer the agent; tools (e.g. <c>record_activity</c>) are what mutate state.
/// </summary>
[McpServerPromptType]
public sealed class PlaceContextPrompts
{
    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "review_work"), Description("Review the project's current work against its requirements (global + project).")]
    public static async Task<string> ReviewWork(
        PlaceContextService svc,
        [Description("The project's GUID id")] string projectId)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            throw new ArgumentException($"Invalid project id: '{projectId}' is not a valid GUID.");
        }
        var (header, requirements) = await GatherAsync(svc, id);
        return PromptText.Render("Mcp.Prompts.ReviewWork", ("header", header), ("requirements", requirements));
    }

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "create_skill"), Description("Guide creating a reusable skill/command for the project for an AI agent (Claude Code or Codex), in that agent's format and following the project's requirements.")]
    public static async Task<string> CreateSkill(
        PlaceContextService svc,
        [Description("The project's GUID id")] string projectId,
        [Description("Short skill name, e.g. 'run-tests' or 'add-endpoint'")] string skillName,
        [Description("Target AI agent: 'claude' (Claude Code) or 'codex' (OpenAI Codex CLI). Defaults to claude.")] string agent = "claude",
        [Description("Optional one-line description of what the skill should do")] string? description = null)
    {
        if (!Guid.TryParse(projectId, out var pid))
        {
            throw new ArgumentException($"Invalid project id: '{projectId}' is not a valid GUID.");
        }
        var (header, requirements) = await GatherAsync(svc, pid);
        var intent = string.IsNullOrWhiteSpace(description) ? "(infer from the project's conventions)" : description!;
        var spec = AgentSkillSpec(agent, skillName);
        return PromptText.Render("Mcp.Prompts.CreateSkill", ("header", header), ("AgentName", spec.AgentName), ("skillName", skillName), ("intent", intent), ("Instructions", spec.Instructions), ("requirements", requirements));
    }

    /// <summary>Per-agent skill format guidance — Claude Code skills vs. Codex prompts/AGENTS.md.</summary>
    private static (string AgentName, string Instructions) AgentSkillSpec(string agent, string skillName)
        => (agent ?? "claude").Trim().ToLowerInvariant() switch
        {
            "codex" or "openai" => ("Codex", PromptText.Render("Mcp.Prompts.PromptCommandFormat", ("skillName", skillName))),
            _ => ("Claude Code", PromptText.Render("Mcp.Prompts.SkillDirectoryFormat", ("skillName", skillName))),
        };

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "record_activity_guidance"), Description("Walk through recording a change correctly into the activity log so it passes the process-trust gates.")]
    public static async Task<string> RecordActivityGuidance(
        PlaceContextService svc,
        [Description("The project's GUID id")] string projectId)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            throw new ArgumentException($"Invalid project id: '{projectId}' is not a valid GUID.");
        }
        var (header, requirements) = await GatherAsync(svc, id);
        return PromptText.Render("Mcp.Prompts.RecordActivity", ("header", header), ("requirements", requirements));
    }

    [Authorize(Policy = "Member")]
    [McpServerPrompt(Name = "onboard"), Description("Load the project's requirements and overview to start a session well-grounded.")]
    public static async Task<string> Onboard(
        PlaceContextService svc,
        [Description("The project's GUID id")] string projectId)
    {
        if (!Guid.TryParse(projectId, out var id))
        {
            throw new ArgumentException($"Invalid project id: '{projectId}' is not a valid GUID.");
        }
        var (header, requirements) = await GatherAsync(svc, id);
        return PromptText.Render("Mcp.Prompts.StartSession", ("header", header), ("requirements", requirements));
    }

    /// <summary>Fetches the project header and effective requirements — with friendly fallbacks.</summary>
    private static async Task<(string Header, string Requirements)> GatherAsync(
        PlaceContextService svc, Guid projectId)
    {
        var overview = await svc.GetProjectOverviewAsync(projectId);
        var reqs = await svc.GetEffectiveRequirementsAsync(projectId);

        var header = $"# Project: {overview.Name}\nPath: {overview.Path}\nStatus: {overview.Status}";
        var requirements = reqs.IsEmpty ? "_No requirements defined yet (global or project)._" : reqs.Markdown;
        return (header, requirements);
    }
}
