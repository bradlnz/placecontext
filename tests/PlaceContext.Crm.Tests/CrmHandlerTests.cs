using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class CrmHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_creates_and_updates_a_project_client()
    {
        var projectId = Guid.NewGuid();
        var repo = new MemoryCrmClientRepository();
        var uow = new RecordingUnitOfWork();
        var clock = new FakeClock(T0);
        var handler = new SaveCrmClientHandler(repo, uow, clock);

        var created = await handler.HandleAsync(new SaveCrmClientCommand(
            projectId, "Ada", "Engines", "ada@example.test", null,
            CustomerLifecycleStage.Lead, null));

        clock.UtcNow = T0.AddDays(1);
        var updated = await handler.HandleAsync(new SaveCrmClientCommand(
            projectId, "Ada Lovelace", "Engines", "ada@example.test", "123",
            CustomerLifecycleStage.Qualified, "Discovery complete", created.Id));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Ada Lovelace", updated.Name);
        Assert.Equal(nameof(CustomerLifecycleStage.Qualified), updated.LifecycleStage);
        Assert.Equal(2, uow.SaveCount);
        Assert.Single(await repo.ListForProjectAsync(projectId));
    }

    [Fact]
    public async Task Move_changes_stage_without_replacing_client_details()
    {
        var projectId = Guid.NewGuid();
        var repo = new MemoryCrmClientRepository();
        var client = CrmClient.Create(
            projectId, "Ada", "Engines", "ada@example.test", null,
            CustomerLifecycleStage.Lead, null, T0);
        await repo.AddAsync(client);
        var handler = new MoveCrmClientHandler(
            repo, new RecordingUnitOfWork(), new FakeClock(T0.AddHours(1)));

        var moved = await handler.HandleAsync(
            new MoveCrmClientCommand(client.Id, CustomerLifecycleStage.Onboarding));

        Assert.Equal("Ada", moved.Name);
        Assert.Equal(nameof(CustomerLifecycleStage.Onboarding), moved.LifecycleStage);
    }

    private sealed class MemoryCrmClientRepository : ICrmClientRepository
    {
        private readonly Dictionary<Guid, CrmClient> _clients = new();

        public Task AddAsync(CrmClient client, CancellationToken ct = default)
        {
            _clients[client.Id] = client;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CrmClient client, CancellationToken ct = default)
        {
            _clients[client.Id] = client;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid clientId, CancellationToken ct = default)
        {
            _clients.Remove(clientId);
            return Task.CompletedTask;
        }

        public Task<CrmClient?> GetByIdAsync(Guid clientId, CancellationToken ct = default)
            => Task.FromResult(_clients.GetValueOrDefault(clientId));

        public Task<IReadOnlyList<CrmClient>> ListForProjectAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmClient>>(
                _clients.Values.Where(c => c.ProjectId == projectId).ToList());
    }
}
