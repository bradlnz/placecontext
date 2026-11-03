using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Skills;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetupHermesHandler : ICommandHandler<SetupHermesCommand, SkillScaffoldView>
{
    private readonly IProjectRepository _projects;
    private readonly ISkillScaffolder _scaffolder;

    public SetupHermesHandler(IProjectRepository projects, ISkillScaffolder scaffolder)
    {
        _projects = projects;
        _scaffolder = scaffolder;
    }

    public async Task<SkillScaffoldView> HandleAsync(SetupHermesCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        var markdown = BuildMarkdown(project.Name.Value, project.Id.Value);
        var path = await _scaffolder.ScaffoldAsync(project.Path, "hermes", markdown, ct);
        return new SkillScaffoldView("hermes", path, markdown);
    }

    private static string BuildMarkdown(string projectName, Guid projectId) => PromptText.Render("Skills.Prompts.JobOrchestration",
                        ("projectName", projectName),
                        ("projectId", projectId.ToString()));
}
