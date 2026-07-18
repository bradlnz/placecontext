using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class SearchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<SearchHandler> SeedAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/home/brad/code/payments"), ProjectName.From("payments"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var ledgers = new InMemoryActivityLogRepository();
        var ledger = ActivityLog.Start(project.Id);
        ledger.Append("Add payment webhook", Author.Agent("claude"), Rationale.None, TestDelta.None,
            ActivityVerification.None, new[] { "a.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var decisions = new InMemoryDecisionRepository();
        await decisions.AddAsync(Decision.Record(project.Id, "Which payment provider?", "Stripe", Rationale.None, T0));

        return new SearchHandler(projects, ledgers, decisions);
    }

    [Theory]
    [InlineData("payments", "project")]
    [InlineData("webhook", "change")]
    [InlineData("provider", "decision")]
    public async Task Search_finds_hits_of_each_kind(string term, string kind)
    {
        var results = await (await SeedAsync()).HandleAsync(new SearchQuery(term));
        Assert.Contains(results.Hits, h => h.Kind == kind);
        Assert.All(results.Hits, h => Assert.StartsWith("/project/", h.Url));
    }

    [Fact]
    public async Task Context_documents_are_not_searched()
    {
        // Context markdown is agent-facing prose — search answers with data nodes, decisions,
        // changes, and artifacts instead.
        var results = await (await SeedAsync()).HandleAsync(new SearchQuery("Stripe"));
        var kinds = results.Hits.Select(h => h.Kind).ToHashSet();
        Assert.DoesNotContain("context", kinds);
        Assert.Contains("decision", kinds);
    }

    [Fact]
    public async Task Short_term_returns_nothing()
        => Assert.Empty((await (await SeedAsync()).HandleAsync(new SearchQuery("a"))).Hits);
}
