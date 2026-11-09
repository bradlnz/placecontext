using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Scaffold a Claude Code skill into a project's working tree, seeded from the project's recorded
/// decisions and context so the skill starts grounded in what PlaceContext knows.
/// </summary>
public sealed record ScaffoldSkillCommand(Guid ProjectId, string SkillName, string? Description)
    : ICommand<SkillScaffoldView>;
