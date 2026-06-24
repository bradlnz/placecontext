using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Skills;

/// <summary>
/// Writes a Claude Code skill into a project's working tree at
/// <c>&lt;repo&gt;/.claude/skills/&lt;name&gt;/SKILL.md</c>. The Application composes the Markdown;
/// this adapter only owns the file I/O.
/// </summary>
public sealed class FileSkillScaffolder : ISkillScaffolder
{
    public async Task<string> ScaffoldAsync(RepoPath repoPath, string skillName, string markdown, CancellationToken ct = default)
    {
        var dir = Path.Combine(repoPath.Value, ".claude", "skills", skillName);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "SKILL.md");
        await File.WriteAllTextAsync(file, markdown, ct);
        return file;
    }
}
