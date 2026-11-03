using PlaceContext.Application.Jobs;
using PlaceContext.Application.Shared;

namespace PlaceContext.Application.Tests;

public sealed class PromptTextTests
{
    [Fact]
    public void Every_markdown_prompt_is_bundled_and_readable()
    {
        var names = typeof(PromptText).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Prompts.") && name.EndsWith(".md", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            var resource = name["PlaceContext.Application.".Length..^3];
            Assert.False(string.IsNullOrWhiteSpace(PromptText.Read(resource)));
        }
        Assert.StartsWith("# Authoring a PlaceContext job", JobAuthoringGuide.Text);
        Assert.Contains("upload_job_code", JobAuthoringGuide.Text);
    }

    [Fact]
    public void Rendering_substitutes_values_once_and_preserves_literal_braces()
    {
        var prompt = PromptText.Render("Skills.Prompts.JobOrchestration",
            ("projectName", "Untrusted {{projectId}} $1"), ("projectId", "project-123"));
        Assert.Contains("**Untrusted {{projectId}} $1** (`projectId: project-123`)", prompt);
        Assert.Contains("/ingest/{eventName}", prompt);
    }

    [Fact]
    public void Missing_resources_and_parameters_fail_explicitly()
    {
        Assert.Throws<InvalidOperationException>(() => PromptText.Read("Missing.Prompts.Unknown"));
        Assert.Throws<ArgumentException>(() => PromptText.Render("Skills.Prompts.JobOrchestration", ("projectName", "Project")));
    }
}
