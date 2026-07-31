using Microsoft.EntityFrameworkCore;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JobChainActionPersistenceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Typed_email_action_round_trips_through_stages_json()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var repository = new EfJobChainRepository(db);
        var email = new SendEmailChainAction(
            "{{client.email}}", "{{client.name}}", "Report {{report.id}}", "Ready",
            "{{attachments}}");
        var chain = JobChain.Create(Guid.NewGuid(), "deliver", null,
            new[] { ChainStage.ForAction(email) }, T0);

        await repository.AddAsync(chain);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await repository.GetByIdAsync(chain.Id);
        var action = Assert.IsType<SendEmailChainAction>(Assert.Single(loaded!.Stages).Action);
        Assert.Equal("{{client.email}}", action.Recipient);
        Assert.Equal("Report {{report.id}}", action.Subject);
        Assert.Equal("{{attachments}}", action.AttachmentPath);
    }

    [Fact]
    public async Task Legacy_flat_job_id_array_still_deserializes()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, tenant);
        var jobId = Guid.NewGuid();
        var row = new JobChainRow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "legacy",
            StagesJson = $"[\"{jobId}\"]",
            CreatedAt = T0,
            UpdatedAt = T0,
        };
        await db.JobChains.AddAsync(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await new EfJobChainRepository(db).GetByIdAsync(row.Id);

        Assert.Equal(jobId, Assert.Single(Assert.Single(loaded!.Stages).JobIds));
        Assert.Null(loaded.Stages[0].Action);
    }
}
