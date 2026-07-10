using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="KubernetesWorkloadRunner"/> helpers that don't need a live cluster.
/// </summary>
public class KubernetesWorkloadRunnerTests
{
    // Regression: the shared SandboxMemory option is written in Docker notation ("256m" = 256 MiB),
    // but a bare "m" in a Kubernetes quantity means milli (10⁻³). Passing it verbatim set the
    // container memory limit to ~0 bytes, so runc OOM-killed the container init before any user code
    // ran (exit 128, StartError, empty logs) — every in-cluster job failed. ToK8sMemory translates it.
    [Theory]
    [InlineData("256m", "256Mi")]   // Docker MiB → k8s MiB (the bug that broke all jobs)
    [InlineData("512m", "512Mi")]
    [InlineData("1g", "1Gi")]       // Docker GiB → k8s GiB
    [InlineData("64k", "64Ki")]     // Docker KiB → k8s KiB
    [InlineData("1024b", "1024")]   // explicit bytes → bare number
    [InlineData("268435456", "268435456")] // bare bytes → unchanged
    [InlineData("256Mi", "256Mi")]  // already a k8s suffix → unchanged
    [InlineData("2Gi", "2Gi")]
    [InlineData("", "")]            // empty → unchanged (limit omitted upstream)
    public void ToK8sMemory_translates_docker_notation(string input, string expected)
        => Assert.Equal(expected, KubernetesWorkloadRunner.ToK8sMemory(input));

    // ── framed /out capture (SplitFramedLogs) ─────────────────────────────────────────────────────

    [Fact]
    public void Framed_logs_split_into_stdout_and_named_files()
    {
        var logs = "{\"ok\":true}\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   "==PC-FILE== listings.pdf 8\n%PDF-1.4\n" +
                   "==PC-FILE== sub/data.csv 3\na,b\n";
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        Assert.Equal("{\"ok\":true}", stdout);
        Assert.Equal(2, files.Count);
        Assert.Equal("listings.pdf", files[0].Name);
        Assert.Equal("%PDF-1.4", files[0].Content);
        Assert.Equal("sub/data.csv", files[1].Name);
        Assert.Equal("a,b", files[1].Content);
    }

    [Fact]
    public void Content_that_looks_like_a_frame_header_is_read_by_length_not_pattern()
    {
        var body = "==PC-FILE== fake.txt 99\ninner";
        var logs = "out\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   $"==PC-FILE== real.txt {System.Text.Encoding.UTF8.GetByteCount(body)}\n{body}\n";
        var (_, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        var f = Assert.Single(files);
        Assert.Equal("real.txt", f.Name);
        Assert.Equal(body, f.Content);
    }

    [Fact]
    public void Logs_without_a_marker_pass_through_unchanged_and_truncated_frames_are_dropped()
    {
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs("plain output\n");
        Assert.Equal("plain output", stdout);
        Assert.Empty(files);

        var truncated = "x\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n==PC-FILE== f.txt 100\nshort";
        var (s2, f2) = KubernetesWorkloadRunner.SplitFramedLogs(truncated);
        Assert.Equal("x", s2);
        Assert.Empty(f2);
    }

    [Fact]
    public void Empty_out_dir_yields_stdout_only()
    {
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs(
            "result\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n");
        Assert.Equal("result", stdout);
        Assert.Empty(files);
    }
}
