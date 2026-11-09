using PlaceContext.Application.Skills;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeSkillScaffolder : ISkillScaffolder
{
    public string? LastMarkdown
    {
        get; private set;
    }
    public Task<string> ScaffoldAsync(ProjectPath projectPath, string skillName, string markdown, CancellationToken ct = default)
    {
        LastMarkdown = markdown;
        return Task.FromResult($"{projectPath.Value}/.claude/skills/{skillName}/SKILL.md");
    }
}
