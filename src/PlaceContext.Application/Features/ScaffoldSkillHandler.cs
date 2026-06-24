using System.Text;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ScaffoldSkillHandler : ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>
{
    private readonly IProjectRepository _projects;
    private readonly IDecisionRepository _decisions;
    private readonly IProjectContextRepository _contexts;
    private readonly ISkillScaffolder _scaffolder;

    public ScaffoldSkillHandler(
        IProjectRepository projects, IDecisionRepository decisions,
        IProjectContextRepository contexts, ISkillScaffolder scaffolder)
    {
        _projects = projects;
        _decisions = decisions;
        _contexts = contexts;
        _scaffolder = scaffolder;
    }

    public async Task<SkillScaffoldView> HandleAsync(ScaffoldSkillCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.SkillName))
            throw new ArgumentException("Skill name must not be empty.", nameof(command));

        var projectId = ProjectId.From(command.ProjectId);
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var decisions = await _decisions.ListForProjectAsync(projectId, ct);
        var context = await _contexts.GetForProjectAsync(projectId, ct);
        var slug = Slugify(command.SkillName);
        var description = string.IsNullOrWhiteSpace(command.Description)
            ? $"Project skill for {project.Name.Value}, scaffolded by PlaceContext."
            : command.Description!.Trim();

        var md = new StringBuilder();
        md.Append("---\n");
        md.Append($"name: {slug}\n");
        md.Append($"description: {description}\n");
        md.Append("---\n\n");
        md.Append($"# {command.SkillName.Trim()}\n\n");
        md.Append($"Scaffolded for **{project.Name.Value}** (`{project.Path.Value}`).\n\n");

        if (decisions.Count > 0)
        {
            md.Append("## Recorded decisions\n\n");
            foreach (var d in decisions.OrderByDescending(d => d.DecidedAt).Take(10))
                md.Append($"- **{d.Question}** → {d.Choice}\n");
            md.Append('\n');
        }

        md.Append("## Project context\n\n");
        md.Append(context is null || context.IsEmpty
            ? "_No context recorded yet. Use `add_context` to capture project knowledge._\n"
            : context.Markdown + "\n");

        var path = await _scaffolder.ScaffoldAsync(project.Path, slug, md.ToString(), ct);
        return new SkillScaffoldView(slug, path, md.ToString());
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') is { Length: > 0 } s ? s : "skill";
    }
}
