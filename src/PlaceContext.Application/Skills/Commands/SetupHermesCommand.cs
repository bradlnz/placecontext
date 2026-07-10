using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Install the "hermes" skill into a project: a job-orchestration playbook for AI agents, written
/// to <c>.claude/skills/hermes/SKILL.md</c>. Hermes teaches the full loop over the PlaceContext MCP
/// tools — author, upload, run, monitor, collect artifacts, automate with triggers/events — so an
/// agent can operate the platform's job system without rediscovering the contract each session.
/// </summary>
public sealed record SetupHermesCommand(Guid ProjectId) : ICommand<SkillScaffoldView>;
