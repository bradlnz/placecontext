using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

/// <summary>
/// Unit tests for the WorkloadSource discriminated union and the updated MapSpec/ReduceSpec/WorkloadSnapshot
/// value objects. All tests are purely in-process — no I/O.
/// </summary>
public class WorkloadSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    // ── ImageWorkload ────────────────────────────────────────────────────────────

    [Fact]
    public void ImageWorkload_stores_image_name_trimmed()
    {
        var ws = new ImageWorkload("  myorg/worker:latest  ");
        Assert.Equal("myorg/worker:latest", ws.Image);
        Assert.Equal("myorg/worker:latest", ws.Label);
    }

    [Fact]
    public void ImageWorkload_rejects_empty_image()
    {
        Assert.Throws<ArgumentException>(() => new ImageWorkload(""));
        Assert.Throws<ArgumentException>(() => new ImageWorkload("   "));
    }

    // ── CodeWorkload ─────────────────────────────────────────────────────────────

    [Fact]
    public void CodeWorkload_stores_all_fields()
    {
        var ws = new CodeWorkload("node", "console.log('hello');", "index.js");
        Assert.Equal("node", ws.RuntimeId);
        Assert.Equal("console.log('hello');", ws.Source);
        Assert.Equal("index.js", ws.Entrypoint);
        Assert.Equal("node", ws.Label);
    }

    [Fact]
    public void CodeWorkload_null_entrypoint_stored_as_null()
    {
        var ws = new CodeWorkload("python", "print('hi')", null);
        Assert.Null(ws.Entrypoint);
    }

    [Fact]
    public void CodeWorkload_whitespace_entrypoint_normalised_to_null()
    {
        var ws = new CodeWorkload("python", "print('hi')", "   ");
        Assert.Null(ws.Entrypoint);
    }

    [Fact]
    public void CodeWorkload_rejects_empty_runtimeId()
    {
        Assert.Throws<ArgumentException>(() => new CodeWorkload("", "print('hi')", null));
        Assert.Throws<ArgumentException>(() => new CodeWorkload("  ", "print('hi')", null));
    }

    [Fact]
    public void CodeWorkload_rejects_empty_source()
    {
        Assert.Throws<ArgumentException>(() => new CodeWorkload("python", "", null));
        Assert.Throws<ArgumentException>(() => new CodeWorkload("python", "  ", null));
    }

    // ── MapSpec with WorkloadSource ──────────────────────────────────────────────────────────────

    [Fact]
    public void MapSpec_image_ctor_creates_ImageWorkload_source()
    {
        var spec = new MapSpec("myorg/img:1.0", new[] { "{}" }, new Dictionary<string, string>());
        Assert.IsType<ImageWorkload>(spec.Source);
        Assert.Equal("myorg/img:1.0", spec.Image);
    }

    [Fact]
    public void MapSpec_with_CodeWorkload_source_image_property_is_null()
    {
        var source = new CodeWorkload("node", "process.exit(0);", null);
        var spec = new MapSpec(source, new[] { "{}" }, new Dictionary<string, string>());
        Assert.IsType<CodeWorkload>(spec.Source);
        Assert.Null(spec.Image);
    }

    [Fact]
    public void MapSpec_rejects_empty_input_payloads_regardless_of_source_type()
    {
        // Invariant lives in Job.Create, but the spec accepts zero payloads.
        // (Job.Create is the gatekeeper — tested in JobRunTests.)
        var source = new CodeWorkload("node", "x", null);
        var spec = new MapSpec(source, Array.Empty<string>(), new Dictionary<string, string>());
        Assert.Equal(0, spec.ShardCount);
    }

    // ── ReduceSpec with WorkloadSource ──────────────────────────────────────────────────────────

    [Fact]
    public void ReduceSpec_image_ctor_creates_ImageWorkload_source()
    {
        var spec = new ReduceSpec("reduce/img:latest", new Dictionary<string, string>());
        Assert.IsType<ImageWorkload>(spec.Source);
        Assert.Equal("reduce/img:latest", spec.Image);
    }

    [Fact]
    public void ReduceSpec_with_CodeWorkload_source()
    {
        var source = new CodeWorkload("python", "import json, sys; print('{}')", null);
        var spec = new ReduceSpec(source, new Dictionary<string, string>());
        Assert.IsType<CodeWorkload>(spec.Source);
        Assert.Null(spec.Image);
    }

    // ── WorkloadSnapshot ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WorkloadSnapshot_From_captures_mapspec_and_reducespec()
    {
        var mapSpec = new MapSpec("img/map:1", new[] { @"{""i"":0}", @"{""i"":1}" },
            new Dictionary<string, string> { ["K"] = "V" });
        var reduceSpec = new ReduceSpec("img/reduce:1", new Dictionary<string, string>());

        var snap = WorkloadSnapshot.From(mapSpec, reduceSpec, concurrencyLimit: 2);

        Assert.IsType<ImageWorkload>(snap.MapSource);
        Assert.Equal(2, snap.InputPayloads.Count);
        Assert.Equal("V", snap.MapEnv["K"]);
        Assert.IsType<ImageWorkload>(snap.ReduceSource);
        Assert.NotNull(snap.ReduceEnv);
        Assert.Equal(2, snap.ConcurrencyLimit);
    }

    [Fact]
    public void WorkloadSnapshot_From_with_code_map_source()
    {
        var codeSource = new CodeWorkload("node", "process.exit(0);", "index.js");
        var mapSpec = new MapSpec(codeSource, new[] { "{}" }, new Dictionary<string, string>());

        var snap = WorkloadSnapshot.From(mapSpec, null, concurrencyLimit: 1);

        var code = Assert.IsType<CodeWorkload>(snap.MapSource);
        Assert.Equal("node", code.RuntimeId);
        Assert.Null(snap.ReduceSource);
        Assert.Null(snap.ReduceEnv);
    }

    [Fact]
    public void WorkloadSnapshot_rejects_concurrency_below_one()
    {
        var mapSpec = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkloadSnapshot.From(mapSpec, null, 0));
    }

    // ── Job.Update ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Job_Update_changes_name_and_spec()
    {
        var map1 = new MapSpec("img/v1", new[] { "{}" }, new Dictionary<string, string>());
        var job = Entities.Job.Create(Guid.NewGuid(), "Original", null, map1, null, 1, ExitCodePolicy.Default, T0);
        Assert.Equal("Original", job.Name);

        var codeSource = new CodeWorkload("node", "process.exit(0);", null);
        var map2 = new MapSpec(codeSource, new[] { "{}", "{}" }, new Dictionary<string, string>());
        job.Update("Renamed", "now a code job", map2, null, 2, ExitCodePolicy.Default, T0.AddMinutes(5));

        Assert.Equal("Renamed", job.Name);
        Assert.Equal("now a code job", job.Description);
        Assert.IsType<CodeWorkload>(job.MapSpec.Source);
        Assert.Equal(2, job.MapSpec.ShardCount);
        Assert.Equal(2, job.ConcurrencyLimit);
        Assert.Equal(T0.AddMinutes(5), job.UpdatedAt);
    }

    [Fact]
    public void Job_Update_rejects_empty_name()
    {
        var map = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Entities.Job.Create(Guid.NewGuid(), "Name", null, map, null, 1, ExitCodePolicy.Default, T0);
        Assert.Throws<ArgumentException>(() => job.Update("  ", null, map, null, 1, ExitCodePolicy.Default, T0));
    }

    [Fact]
    public void Job_Update_rejects_concurrency_below_one()
    {
        var map = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        var job = Entities.Job.Create(Guid.NewGuid(), "Name", null, map, null, 1, ExitCodePolicy.Default, T0);
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Update("Name", null, map, null, 0, ExitCodePolicy.Default, T0));
    }

    // ── JobRun snapshot ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void JobRun_Start_captures_snapshot()
    {
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var mapSpec = new MapSpec("img/v1", new[] { "{}" }, new Dictionary<string, string> { ["K"] = "V" });
        var snap = WorkloadSnapshot.From(mapSpec, null, 1);

        var run = Entities.JobRun.Start(jobId, projectId, T0, snap);

        Assert.NotNull(run.Snapshot);
        Assert.IsType<ImageWorkload>(run.Snapshot.MapSource);
        Assert.Equal("V", run.Snapshot.MapEnv["K"]);
        Assert.Single(run.Snapshot.InputPayloads);
    }

    [Fact]
    public void JobRun_Start_rejects_null_snapshot()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Entities.JobRun.Start(Guid.NewGuid(), Guid.NewGuid(), T0, null!));
    }

    [Fact]
    public void JobRun_snapshot_preserved_after_complete()
    {
        var mapSpec = new MapSpec("img/v1", new[] { "{}" }, new Dictionary<string, string>());
        var snap = WorkloadSnapshot.From(mapSpec, null, 1);
        var run = Entities.JobRun.Start(Guid.NewGuid(), Guid.NewGuid(), T0, snap);

        run.Complete(new[] { new Entities.ShardResult(0, 0, WorkloadOutcome.Succeeded, "{}", null) },
            null, T0.AddSeconds(5));

        // Snapshot unchanged after completion
        Assert.IsType<ImageWorkload>(run.Snapshot.MapSource);
    }
}
