using System.Text;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Application.Tests;

public class PostJobArtifactsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    private static (Job job, JobRun run) Sample()
    {
        var mapSpec = new MapSpec("img", new[] { "{}", "{}" }, new Dictionary<string, string>());
        var job = Job.Create(Guid.NewGuid(), "nightly-etl", null, mapSpec, null, 1,
            new ExitCodePolicy(new[] { 0 }, Array.Empty<int>()), T0);
        var snap = WorkloadSnapshot.From(mapSpec, null, 1);
        var run = JobRun.Start(job.Id, job.ProjectId, T0, snap);
        run.Complete(new[]
        {
            new ShardResult(0, 0, WorkloadOutcome.Succeeded, "{\"rows\":3}", "ok"),
            new ShardResult(1, 1, WorkloadOutcome.Failed, null, "boom"),
        }, null, T0.AddSeconds(5));
        return (job, run);
    }

    private static string Text(PostJobArtifacts.BuiltFile f) => Encoding.UTF8.GetString(f.Content);

    [Fact]
    public void Csv_has_a_header_and_a_row_per_shard()
    {
        var (_, run) = Sample();
        var csv = Text(PostJobArtifacts.Csv(run));
        Assert.Contains("shard,exit_code,outcome,artifact", csv);
        Assert.Contains("0,0,Succeeded", csv);
        Assert.Contains("1,1,Failed", csv);
    }

    [Fact]
    public void Html_report_is_self_contained_with_embedded_chart()
    {
        var (job, run) = Sample();
        var f = PostJobArtifacts.HtmlReport(job, run);
        var html = Text(f);
        Assert.StartsWith("text/html", f.ContentType);
        Assert.Contains("<!doctype html>", html);
        Assert.Contains("nightly-etl", html);
        Assert.Contains("<svg", html);          // chart embedded inline
        Assert.DoesNotContain("<script", html); // no CDN/JS — opens offline
    }

    [Fact]
    public void Chart_is_offline_inline_svg()
    {
        var (job, run) = Sample();
        var html = Text(PostJobArtifacts.Chart(job, run));
        Assert.Contains("<svg", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void RawBundle_yields_one_file_per_produced_artifact()
    {
        var (_, run) = Sample();
        var files = PostJobArtifacts.RawBundle(run).ToList();
        Assert.Single(files); // only shard 0 produced an artifact
        Assert.Equal("raw/shard-0/result.json", files[0].FileName);
        Assert.Equal("application/json", files[0].ContentType);
    }
}
