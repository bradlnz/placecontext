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
    // Frames are base64 (v2): header '==PC-FILE== name rawByteCount', payload lines, then the end
    // marker. Base64 keeps the pod-log channel ASCII-safe so binary files survive byte-exact.

    private static string Frame(string name, byte[] bytes, int wrapAt = 76)
    {
        var b64 = Convert.ToBase64String(bytes);
        var wrapped = string.Join("\n", b64.Chunk(wrapAt).Select(c => new string(c)));
        return $"==PC-FILE== {name} {bytes.Length}\n{wrapped}\n{KubernetesWorkloadRunner.FileEndMarker}\n";
    }

    [Fact]
    public void Framed_logs_split_into_stdout_and_named_files()
    {
        var logs = "{\"ok\":true}\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   Frame("listings.txt", System.Text.Encoding.UTF8.GetBytes("%PDF-1.4")) +
                   Frame("sub/data.csv", System.Text.Encoding.UTF8.GetBytes("a,b"));
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        Assert.Equal("{\"ok\":true}", stdout);
        Assert.Equal(2, files.Count);
        Assert.Equal("listings.txt", files[0].Name);
        Assert.Equal("%PDF-1.4", files[0].Content);
        Assert.False(files[0].IsBinary);
        Assert.Equal("sub/data.csv", files[1].Name);
        Assert.Equal("a,b", files[1].Content);
    }

    [Fact]
    public void Binary_files_round_trip_byte_exact_as_base64_artifacts()
    {
        // A real PDF header includes bytes that are not valid UTF-8 — the exact content the old
        // text pipeline corrupted. It must come back byte-identical, flagged binary.
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x00, 0xFF };
        var logs = "done\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" + Frame("report.pdf", pdf, wrapAt: 8);
        var (_, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        var f = Assert.Single(files);
        Assert.Equal("report.pdf", f.Name);
        Assert.True(f.IsBinary);
        Assert.Equal(pdf, f.GetBytes());
    }

    [Fact]
    public void Payload_lines_that_look_like_frame_headers_cannot_occur_and_size_mismatches_are_dropped()
    {
        // Base64 payloads can never contain a header/end-marker line, so parsing is unambiguous.
        // A frame whose decoded size disagrees with its header (mid-payload log loss) is dropped.
        var good = System.Text.Encoding.UTF8.GetBytes("real content");
        var logs = "out\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n" +
                   $"==PC-FILE== bad.bin 9999\n{Convert.ToBase64String(good)}\n{KubernetesWorkloadRunner.FileEndMarker}\n" +
                   Frame("good.txt", good);
        var (_, files) = KubernetesWorkloadRunner.SplitFramedLogs(logs);
        var f = Assert.Single(files);
        Assert.Equal("good.txt", f.Name);
        Assert.Equal("real content", f.Content);
    }

    [Fact]
    public void Logs_without_a_marker_pass_through_unchanged_and_truncated_frames_are_dropped()
    {
        var (stdout, files) = KubernetesWorkloadRunner.SplitFramedLogs("plain output\n");
        Assert.Equal("plain output", stdout);
        Assert.Empty(files);

        // Log cut off before the end marker ⇒ the partial frame is dropped, stdout survives.
        var truncated = "x\n" + KubernetesWorkloadRunner.ArtifactsMarker + "\n==PC-FILE== f.txt 100\nc2hvcnQ=";
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
