using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class GenerateReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(GenerateReportHandler handler, Guid projectId, InMemoryWorkItemRepository workItems)> BuildAsync(
        ILlmGateway llm)
    {
        var projects = new InMemoryProjectRepository();
        var contexts = new InMemoryProjectContextRepository();
        var workItems = new InMemoryWorkItemRepository();
        var clock = new FakeClock(T0);

        var project = Project.Discover(ProjectPath.From("/work/acme-case"), ProjectName.From("acme-case"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var handler = new GenerateReportHandler(
            projects, contexts, new InMemoryRequirementsRepository(), new InMemoryDecisionRepository(),
            workItems, new InMemoryActivityLogRepository(), new InMemoryRiskAssessmentRepository(),
            new InMemoryUsageRepository(), new InMemoryReportTemplateRepository(),
            new TokenCostCalculator(), llm, new RecordingUnitOfWork(), clock);

        return (handler, project.Id.Value, workItems);
    }

    [Fact]
    public async Task Default_report_is_deterministic_when_no_llm_configured()
    {
        var (handler, projectId, _) = await BuildAsync(new FakeLlmGateway(enabled: false));

        var report = await handler.HandleAsync(new GenerateReportCommand(projectId));

        Assert.False(report.GeneratedByLlm);
        Assert.Equal(BuiltInReportTemplates.OnboardingBriefName, report.TemplateName);
        Assert.Contains("# Onboarding Brief — acme-case", report.Markdown);
        Assert.Contains("## Action plan", report.Markdown);
        // An empty project surfaces the "capture context" action.
        Assert.Contains(report.Actions, a => a.Title.Contains("context", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Llm_polishes_when_enabled()
    {
        var (handler, projectId, _) = await BuildAsync(new FakeLlmGateway(enabled: true));

        var report = await handler.HandleAsync(new GenerateReportCommand(projectId));

        Assert.True(report.GeneratedByLlm);
        Assert.StartsWith("POLISHED:", report.Markdown);
    }

    [Fact]
    public async Task CreateWorkItems_queues_the_action_plan()
    {
        var (handler, projectId, workItems) = await BuildAsync(new FakeLlmGateway(enabled: false));

        var report = await handler.HandleAsync(new GenerateReportCommand(projectId, null, CreateWorkItems: true));

        Assert.NotEmpty(report.CreatedWorkItems);
        var queued = await workItems.ListForProjectAsync(ProjectId.From(projectId));
        Assert.Equal(report.CreatedWorkItems.Count, queued.Count);
    }

    [Fact]
    public async Task Unknown_template_name_throws()
    {
        var (handler, projectId, _) = await BuildAsync(new FakeLlmGateway(enabled: false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new GenerateReportCommand(projectId, "Nonexistent Report")));
    }
}
