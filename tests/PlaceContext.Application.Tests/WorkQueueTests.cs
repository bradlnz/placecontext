using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class WorkQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(AddWorkItemHandler add, NextWorkItemHandler next, CompleteWorkItemHandler complete, GetWorkItemsHandler get, Guid pid)> SeedAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(RepoPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var repo = new InMemoryWorkItemRepository();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        return (
            new AddWorkItemHandler(projects, repo, uow, clock),
            new NextWorkItemHandler(repo, uow, clock),
            new CompleteWorkItemHandler(repo, uow, clock),
            new GetWorkItemsHandler(repo),
            project.Id.Value);
    }

    [Fact]
    public async Task Next_claims_highest_priority_first_then_completes()
    {
        var (add, next, complete, _, pid) = await SeedAsync();
        await add.HandleAsync(new AddWorkItemCommand(pid, "tidy logs", null, "Low"));
        await add.HandleAsync(new AddWorkItemCommand(pid, "fix prod bug", null, "High"));

        var claimed = await next.HandleAsync(new NextWorkItemCommand(pid));
        Assert.NotNull(claimed);
        Assert.Equal("fix prod bug", claimed!.Title);   // High before Low
        Assert.Equal("InProgress", claimed.Status);

        var done = await complete.HandleAsync(new CompleteWorkItemCommand(claimed.Id));
        Assert.Equal("Done", done.Status);

        var second = await next.HandleAsync(new NextWorkItemCommand(pid));
        Assert.Equal("tidy logs", second!.Title);
        Assert.Null(await next.HandleAsync(new NextWorkItemCommand(pid))); // queue empty
    }

    [Fact]
    public async Task Next_returns_null_when_queue_is_empty()
    {
        var (_, next, _, _, pid) = await SeedAsync();
        Assert.Null(await next.HandleAsync(new NextWorkItemCommand(pid)));
    }
}
