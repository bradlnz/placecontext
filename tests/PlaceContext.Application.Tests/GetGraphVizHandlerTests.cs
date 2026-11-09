using System.Reflection;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class GetGraphVizHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Graph_contains_only_tables_and_mappings_for_job_and_workflow_sources(bool enabled)
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
        var store = DispatchProxy.Create<IProjectDataStore, MetadataStore>();
        ((MetadataStore)(object)store).ProjectId = projectId;
        var handler = new GetGraphVizHandler(projects, store, mappings);

        var graph = await handler.HandleAsync(new(projectId));

        Assert.Equal(5, graph.NodeCount);
        Assert.Equal(2, graph.LinkCount);
        Assert.All(graph.Nodes, node => Assert.Contains(node.Kind, new[] { "Mapping", "Table" }));
        Assert.Contains(graph.Nodes, node => node.Id == "table:unmapped" && node.Degree == 0);
        Assert.Contains(graph.Nodes, node => node.Id == "table:pending_table" && node.Content!.Contains("not been created"));
        var mapped = Assert.Single(graph.Nodes, node => node.Id == $"mapping:{mapping.Id:N}");
        Assert.Equal(1, mapped.Degree);
        Assert.Contains("id → order_id (text)", mapped.Content);
        var confidence = enabled ? "Direct" : "Ambiguous";
        Assert.Contains(graph.Links, link => link.Source == mapped.Id && link.Target == "table:orders" && link.Confidence == confidence);
        Assert.DoesNotContain(graph.Nodes, node => node.Id.Contains("other_project"));
    }

    [Fact]
    public async Task Unknown_project_is_rejected_before_reading_metadata()
    {
        var handler = new GetGraphVizHandler(new InMemoryProjectRepository(), null!, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new(Guid.NewGuid())));
        Assert.Equal(Permission.DataRead, new GetGraphVizQuery(Guid.NewGuid()).RequiredPermission);
        Assert.Equal(Permission.ArtifactsView, new ListProjectArtifactsQuery(Guid.NewGuid()).RequiredPermission);
    }

    public class MetadataStore : DispatchProxy
    {
        public Guid ProjectId { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal(nameof(IProjectDataStore.ListTablesAsync), method!.Name);
            Assert.Equal(ProjectId, args![0]);
            return Task.FromResult<IReadOnlyList<ProjectTableInfo>>([new("orders", 500), new("unmapped", 0)]);
        }
    }
}
