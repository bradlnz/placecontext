using System.Reflection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class GetGraphVizHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Graph_contains_tables_mappings_and_registered_entities(bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/graph"), ProjectName.From("Graph"), now);
        await projects.AddAsync(project);
        var projectId = project.Id.Value;
        var jobId = Guid.NewGuid();
        var chainId = Guid.NewGuid();
        var mappings = new InMemoryDataMappingRepository();
        var mapping = DataMapping.Create(projectId, jobId, "orders", "rows", [new("id", "order_id", "text")], now, enabled);
        await mappings.AddAsync(mapping);
        var chainMapping = DataMapping.Create(projectId, chainId, "pending_table", null, [new("$", "payload", "text")], now, sourceKind: "chain");
        await mappings.AddAsync(chainMapping);
        await mappings.AddAsync(DataMapping.Create(Guid.NewGuid(), jobId, "other_project", null, [new("$", "payload", "text")], now));
        var entities = new FakeEntities([
            DataEntity.Create(projectId, "Orders", "orders", "order_id",
                [new("customer_id", "Customers", "id")], now),
            DataEntity.Create(projectId, "Customers", "customers", "id", [], now)]);
        var store = DispatchProxy.Create<IProjectDataStore, MetadataStore>();
        ((MetadataStore)(object)store).ProjectId = projectId;
        var handler = new GetGraphVizHandler(projects, store, mappings, entities);

        var graph = await handler.HandleAsync(new(projectId));

        Assert.Equal(9, graph.NodeCount);
        Assert.Equal(7, graph.LinkCount);
        Assert.All(graph.Nodes, node => Assert.Contains(node.Kind, new[] { "Mapping", "Table", "Entity", "Item" }));
        Assert.Contains(graph.Nodes, node => node.Id == "table:unmapped" && node.Degree == 0);
        Assert.Contains(graph.Nodes, node => node.Id == "table:pending_table" && node.Content!.Contains("not been created"));
        var mapped = Assert.Single(graph.Nodes, node => node.Id == $"mapping:{mapping.Id:N}");
        Assert.Equal(1, mapped.Degree);
        Assert.Contains("id → order_id (text)", mapped.Content);
        var confidence = enabled ? "Direct" : "Ambiguous";
        Assert.Contains(graph.Links, link => link.Source == mapped.Id && link.Target == "table:orders" && link.Confidence == confidence);
        var orderEntity = Assert.Single(graph.Nodes, node => node.Id == $"entity:{entities.Items[0].Id:N}");
        Assert.Contains("customer_id → Customers.id", orderEntity.Content);
        Assert.Contains(graph.Links, link => link.Source == orderEntity.Id && link.Target == "table:orders");
        Assert.Contains(graph.Links, link => link.Source == orderEntity.Id && link.Target == $"entity:{entities.Items[1].Id:N}");
        var orderItem = Assert.Single(graph.Nodes, node => node.Id == $"item:{entities.Items[0].Id:N}:0");
        Assert.Contains("order_id: order-1", orderItem.Content);
        Assert.Contains(graph.Links, link => link.Source == orderItem.Id && link.Target == orderEntity.Id);
        Assert.DoesNotContain(graph.Nodes, node => node.Id.Contains("other_project"));
    }

    [Fact]
    public async Task Unknown_project_is_rejected_before_reading_metadata()
    {
        var handler = new GetGraphVizHandler(new InMemoryProjectRepository(), null!, null!, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new(Guid.NewGuid())));
        Assert.Equal(Permission.DataRead, new GetGraphVizQuery(Guid.NewGuid()).RequiredPermission);
        Assert.Equal(Permission.ArtifactsView, new ListProjectArtifactsQuery(Guid.NewGuid()).RequiredPermission);
    }

    public class MetadataStore : DispatchProxy
    {
        public Guid ProjectId { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IProjectDataStore.ReadTableAsync))
            {
                return Task.FromResult(new ProjectTableReadResult(
                    ["order_id", "customer_id"], ["text", "text"], [["order-1", "customer-1"]], 1, false));
            }
            Assert.Equal(nameof(IProjectDataStore.ListTablesAsync), method.Name);
            Assert.Equal(ProjectId, args![0]);
            return Task.FromResult<IReadOnlyList<ProjectTableInfo>>([new("orders", 500), new("unmapped", 0)]);
        }
    }

    private sealed class FakeEntities(IReadOnlyList<DataEntity> items) : IDataEntityRepository
    {
        public IReadOnlyList<DataEntity> Items { get; } = items;
        public Task AddAsync(DataEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(DataEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid entityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DataEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DataEntity>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(Items.Where(item => item.ProjectId == projectId).ToList() as IReadOnlyList<DataEntity>);
    }
}
