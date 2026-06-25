using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Writes a Claude Code skill scaffold (SKILL.md and folder) into a project's working tree.</summary>
public interface ISkillScaffolder
{
    /// <summary>Writes the skill and returns the absolute path of the SKILL.md it created.</summary>
    Task<string> ScaffoldAsync(ProjectPath projectPath, string skillName, string markdown, CancellationToken ct = default);
}
