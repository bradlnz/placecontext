using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Skills;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Skills;

/// <summary>
/// Writes a Claude Code skill into a project's working tree at
/// <c>&lt;repo&gt;/.claude/skills/&lt;name&gt;/SKILL.md</c>. The Application composes the Markdown;
/// this adapter only owns the file I/O.
/// </summary>
public sealed class FileSkillScaffolder(IOptions<RunBillingDeploymentOptions>? hosting = null) : ISkillScaffolder
{
    public async Task<string> ScaffoldAsync(ProjectPath projectPath, string skillName, string markdown, CancellationToken ct = default)
    {
        if (hosting?.Value.ManagedHosting == true)
            throw new UnauthorizedAccessException("Managed workspaces cannot write skills to the platform host.");
        if (string.IsNullOrWhiteSpace(skillName) || skillName.Length > 100 || skillName.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use a skill name containing only letters, digits, hyphens or underscores.", nameof(skillName));
        var dir = Path.Combine(projectPath.Value, ".claude", "skills", skillName);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "SKILL.md");
        await File.WriteAllTextAsync(file, markdown, ct);
        return file;
    }
}
