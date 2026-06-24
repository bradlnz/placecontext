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
        var project = Project.Discover(RepoPath.From("/home/brad/code/payments"), ProjectName.From("payments"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var ledgers = new InMemoryChangeLedgerRepository();
        var ledger = ChangeLedger.Start(project.Id);
        ledger.Append("Add payment webhook", Author.Agent("claude"), Rationale.None, TestDelta.None,
            DebtDelta.None, ChangeVerification.None, new[] { "a.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var contexts = new InMemoryProjectContextRepository();
        var ctx = ProjectContext.Start(project.Id, T0);
        ctx.Append("# Notes\nStripe webhook signing secret rotates monthly.", T0);
        await contexts.SaveAsync(ctx);

        var decisions = new InMemoryDecisionRepository();
        await decisions.AddAsync(Decision.Record(project.Id, "Which payment provider?", "Stripe", Rationale.None, T0));

        return new SearchHandler(projects, ledgers, contexts, decisions);
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
    public async Task Stripe_matches_both_context_and_decision()
    {
        var results = await (await SeedAsync()).HandleAsync(new SearchQuery("Stripe"));
        var kinds = results.Hits.Select(h => h.Kind).ToHashSet();
        Assert.Contains("context", kinds);
        Assert.Contains("decision", kinds);
    }

    [Fact]
    public async Task Short_term_returns_nothing()
        => Assert.Empty((await (await SeedAsync()).HandleAsync(new SearchQuery("a"))).Hits);
}
