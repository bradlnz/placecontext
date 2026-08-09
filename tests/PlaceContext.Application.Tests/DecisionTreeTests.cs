using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Data.Integration;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class DecisionTreeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assembler_builds_root_decision_change_and_hotspot_nodes()
    {
        var pid = ProjectId.New();
        var ledger = ActivityLog.Start(pid);
        for (var i = 1; i <= 3; i++)
            ledger.Append($"change {i}", Author.Agent("claude"), Rationale.None, TestDelta.None,
                ActivityVerification.None, new[] { "core.cs" }, Array.Empty<GraphNodeId>(), T0);

        var decisions = new[] { Decision.Record(pid, "Use EF?", "Yes", Rationale.None, T0) };
        var activity = new[] { new ToolActivity("record_activity", false), new ToolActivity("query_graph", true) };

        var tree = new DecisionTreeAssembler().Assemble(ProjectName.From("alpha"), decisions, ledger, activity);

        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Root);
        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Decision);
        Assert.Equal(3, tree.Nodes.Count(n => n.Kind == TreeNodeKind.Change));

        // core.cs was touched by 3 changes → a churn hotspot (god node).
        var hotspots = tree.Hotspots();
        var hot = Assert.Single(hotspots);
        Assert.Equal(3, hot.Degree);
        Assert.Equal(1, tree.ToMetrics().GodNodeCount);
    }

    [Fact]
    public void Tool_call_activity_is_excluded_from_the_dependency_graph()
    {
        var pid = ProjectId.New();
        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            new[] { new ToolActivity("query_graph", false), new ToolActivity("record_activity", true) });

        // MCP tool calls are transient access, not structural dependencies — the assembler ignores them,
        // so (with no decisions or changes) they contribute no coupling edges and the low-confidence
        // ratio stays zero rather than being inflated by tool traffic.
        Assert.Equal(0.0, tree.ToMetrics().LowConfidenceLinkRatio);
    }

    [Fact]
    public void Run_outputs_become_brain_nodes_cross_linked_by_similarity()
    {
        var pid = ProjectId.New();

        // Two near-identical vectors (should link) + one orthogonal (should not).
        var runOutputs = new[]
        {
            new RunOutputNode("aaaaaaaa", "## Organized run output: nightly etl", new[] { 1f, 0f, 0f }),
            new RunOutputNode("bbbbbbbb", "## Organized run output: nightly etl rerun", new[] { 0.9f, 0.1f, 0f }),
            new RunOutputNode("cccccccc", "## Organized run output: image resize", new[] { 0f, 0f, 1f }),
        };

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), runOutputs);

        // Each embedded run output is woven in as a JobRunOutput node, with the markdown header stripped.
        Assert.Equal(3, tree.Nodes.Count(n => n.Kind == TreeNodeKind.JobRunOutput));
        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.JobRunOutput && n.Label == "Organized run output: nightly etl");

        // Exactly one semantic cross-link: the two near-identical outputs; the orthogonal one stays unlinked.
        var crossLinks = tree.Edges.Count(e =>
            e.ParentId.StartsWith("runoutput:") && e.ChildId.StartsWith("runoutput:"));
        Assert.Equal(1, crossLinks);

        // The brain vocabulary surfaces the run-output nodes.
        Assert.Contains("brain", tree.Answer("show me the brain").ToLowerInvariant());
    }

    [Fact]
    public void Answer_reports_hotspots()
    {
        var pid = ProjectId.New();
        var ledger = ActivityLog.Start(pid);
        for (var i = 1; i <= 4; i++)
            ledger.Append($"c{i}", Author.Agent("claude"), Rationale.None, TestDelta.None,
                ActivityVerification.None, new[] { "hot.cs" }, Array.Empty<GraphNodeId>(), T0);

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ledger, Array.Empty<ToolActivity>());

        Assert.Contains("hot.cs", tree.Answer("what are the hotspots?"));
    }

    [Fact]
    public void Lineage_links_chains_jobs_and_the_tables_jobs_write()
    {
        var pid = ProjectId.New();
        var jobA = new DataJobSummary(Guid.NewGuid(), pid.Value, "scrape", null, "json");
        var jobB = new DataJobSummary(Guid.NewGuid(), pid.Value, "feasibility", null, "json");
        var chain = new DataChainSummary(Guid.NewGuid(), pid.Value, "nightly", null,
            new[] { new DataChainStageSummary(new[] { jobA.Id }), new DataChainStageSummary(new[] { jobB.Id }) });
        var mapping = DataMapping.Create(pid.Value, jobB.Id, "feasibility_matrix", null,
            new[] { new DataFieldMapping("$.margin", "margin_pct", "numeric") }, T0);

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), runOutputs: null,
            jobs: new[] { jobA, jobB }, chains: new[] { chain },
            mappings: new[] { mapping }, tables: new[] { "feasibility_matrix", "job_run_data" });

        var chainId = "chain:" + chain.Id.ToString("N");
        var jobAId = "job:" + jobA.Id.ToString("N");
        var jobBId = "job:" + jobB.Id.ToString("N");

        Assert.Equal(2, tree.Nodes.Count(n => n.Kind == TreeNodeKind.Job));
        Assert.Single(tree.Nodes, n => n.Kind == TreeNodeKind.Chain);
        Assert.Equal(2, tree.Nodes.Count(n => n.Kind == TreeNodeKind.Table));

        // Chain membership, stage-to-stage dependency, job→table lineage.
        Assert.Contains(tree.Edges, e => e.ParentId == "root" && e.ChildId == chainId);
        Assert.Contains(tree.Edges, e => e.ParentId == chainId && e.ChildId == jobAId);
        Assert.Contains(tree.Edges, e => e.ParentId == jobAId && e.ChildId == jobBId);
        Assert.Contains(tree.Edges, e => e.ParentId == jobBId && e.ChildId == "table:feasibility_matrix");

        // Chained jobs do NOT also hang off the root; the unmapped table does.
        Assert.DoesNotContain(tree.Edges, e => e.ParentId == "root" && e.ChildId == jobAId);
        Assert.DoesNotContain(tree.Edges, e => e.ParentId == "root" && e.ChildId == jobBId);
        Assert.Contains(tree.Edges, e => e.ParentId == "root" && e.ChildId == "table:job_run_data");
    }

    [Fact]
    public void Unchained_jobs_hang_off_the_root()
    {
        var pid = ProjectId.New();
        var job = new DataJobSummary(Guid.NewGuid(), pid.Value, "ad-hoc", null, "json");

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), jobs: new[] { job });

        Assert.Contains(tree.Edges, e => e.ParentId == "root" && e.ChildId == "job:" + job.Id.ToString("N"));
    }

    [Fact]
    public void Run_outputs_link_to_their_job_when_the_job_is_known()
    {
        var pid = ProjectId.New();
        var job = new DataJobSummary(Guid.NewGuid(), pid.Value, "nightly etl", null, "json");
        var runOutputs = new[]
        {
            new RunOutputNode("aaaaaaaa", "## Organized run output: nightly etl", new[] { 1f, 0f }, job.Id),
        };

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), runOutputs, jobs: new[] { job });

        var jobId = "job:" + job.Id.ToString("N");
        Assert.Contains(tree.Edges, e => e.ParentId == jobId && e.ChildId == "runoutput:aaaaaaaa");
        Assert.DoesNotContain(tree.Edges, e => e.ParentId == "root" && e.ChildId == "runoutput:aaaaaaaa");
    }

    [Fact]
    public void Entities_link_to_their_table_and_to_related_entities()
    {
        var pid = ProjectId.New();
        var sites = DataEntity.Create(pid.Value, "Sites", "feasibility_matrix", "address",
            Array.Empty<EntityRelation>(), T0);
        var listings = DataEntity.Create(pid.Value, "Listings", "listings", "address",
            new[] { new EntityRelation("address", "Sites", "address") }, T0);

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), entities: new[] { sites, listings });

        var sitesId = "entity:" + sites.Id.ToString("N");
        var listingsId = "entity:" + listings.Id.ToString("N");

        Assert.Equal(2, tree.Nodes.Count(n => n.Kind == TreeNodeKind.Entity));
        // Sites' table is not in the graph → hangs off the root; Listings' table isn't either.
        Assert.Contains(tree.Edges, e => e.ParentId == "root" && e.ChildId == sitesId);
        // Declared relation: Listings → Sites.
        Assert.Contains(tree.Edges, e => e.ParentId == listingsId && e.ChildId == sitesId);
    }

    [Fact]
    public void Entities_attach_to_their_table_node_when_the_table_is_in_the_graph()
    {
        var pid = ProjectId.New();
        var sites = DataEntity.Create(pid.Value, "Sites", "feasibility_matrix", "address",
            Array.Empty<EntityRelation>(), T0);

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(),
            tables: new[] { "feasibility_matrix" }, entities: new[] { sites });

        var sitesId = "entity:" + sites.Id.ToString("N");
        Assert.Contains(tree.Edges, e => e.ParentId == "table:feasibility_matrix" && e.ChildId == sitesId);
        Assert.DoesNotContain(tree.Edges, e => e.ParentId == "root" && e.ChildId == sitesId);
    }

    [Fact]
    public void Job_runs_and_artifacts_link_to_their_job()
    {
        var pid = ProjectId.New();
        var job = new DataJobSummary(Guid.NewGuid(), pid.Value, "scrape", null, "json");
        var run = new DataRunSummary(Guid.NewGuid(), job.Id, "Succeeded", T0);
        var artifact = new DataArtifactSummary(
            Guid.NewGuid(), run.Id, job.Id, "report.html", "HtmlReport", "text/html");

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), jobs: new[] { job }, runs: new[] { run }, artifacts: new[] { artifact });

        var jobId = "job:" + job.Id.ToString("N");
        var runNodeId = "run:" + run.Id.ToString("N")[..8];
        var artifactNodeId = "artifact:" + artifact.Id.ToString("N");

        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.JobRun && n.Id == runNodeId);
        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Artifact && n.Id == artifactNodeId);
        Assert.Contains(tree.Edges, e => e.ParentId == jobId && e.ChildId == runNodeId);
        Assert.Contains(tree.Edges, e => e.ParentId == runNodeId && e.ChildId == artifactNodeId);
    }

    [Fact]
    public void Record_link_clusters_create_address_nodes_linked_to_entities()
    {
        var pid = ProjectId.New();
        var sites = DataEntity.Create(pid.Value, "Sites", "feasibility_matrix", "address",
            Array.Empty<EntityRelation>(), T0);
        var listings = DataEntity.Create(pid.Value, "Listings", "listings", "address",
            Array.Empty<EntityRelation>(), T0);
        var cluster = new RecordLinkCluster("address", "123 main st", "123 Main St", new[]
        {
            new RecordLinkClusterOccurrence("feasibility_matrix", "address", "row-1"),
            new RecordLinkClusterOccurrence("listings", "address", "row-2"),
        });

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), entities: new[] { sites, listings }, linkClusters: new[] { cluster });

        var sitesId = "entity:" + sites.Id.ToString("N");
        var listingsId = "entity:" + listings.Id.ToString("N");
        var linkId = "link:address:123 main st";

        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Address && n.Id == linkId);
        Assert.Contains(tree.Edges, e => e.ParentId == sitesId && e.ChildId == linkId);
        Assert.Contains(tree.Edges, e => e.ParentId == listingsId && e.ChildId == linkId);
    }

    [Fact]
    public void Entity_aligned_nodes_do_not_inflate_coupling_metrics()
    {
        var pid = ProjectId.New();
        var job = new DataJobSummary(Guid.NewGuid(), pid.Value, "scrape", null, "json");
        var run = new DataRunSummary(Guid.NewGuid(), job.Id, "Succeeded", T0);
        var artifact = new DataArtifactSummary(
            Guid.NewGuid(), run.Id, job.Id, "report.html", "HtmlReport", "text/html");

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), jobs: new[] { job }, runs: new[] { run }, artifacts: new[] { artifact });

        // With no decisions/changes, the structural coupling low-confidence ratio must stay zero.
        Assert.Equal(0.0, tree.ToMetrics().LowConfidenceLinkRatio);
    }
}
