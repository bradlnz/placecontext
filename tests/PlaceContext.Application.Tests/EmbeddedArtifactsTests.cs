using System.Text;
using PlaceContext.Application.Features;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Artifacts embedded in a job's returned JSON ("artifacts": [{filename, base64|content…}]) are
/// recognised by shape and saved like /out files — the only channel image workloads have in-cluster,
/// and the shape job authors reach for naturally.
/// </summary>
public class EmbeddedArtifactsTests
{
    private static readonly byte[] PdfBytes = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3 };

    [Fact]
    public void Filename_plus_base64_yields_a_binary_artifact_byte_exact()
    {
        var json = $$"""{"ok":true,"artifacts":[{"filename":"test-report.pdf","base64":"{{Convert.ToBase64String(PdfBytes)}}"}]}""";
        var a = Assert.Single(EmbeddedArtifacts.Extract(json));
        Assert.Equal("test-report.pdf", a.Name);
        Assert.True(a.IsBinary);
        Assert.Equal(PdfBytes, a.GetBytes());
    }

    [Fact]
    public void Base64_that_decodes_to_clean_text_stays_a_text_artifact()
    {
        var json = $$"""{"artifacts":[{"name":"data.csv","base64":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes("a,b\n1,2"))}}"}]}""";
        var a = Assert.Single(EmbeddedArtifacts.Extract(json));
        Assert.False(a.IsBinary);
        Assert.Equal("a,b\n1,2", a.Content);
    }

    [Fact]
    public void Plain_content_data_uri_and_encoding_marker_are_all_recognised()
    {
        var json = $$"""
        {"artifacts":[
          {"path":"notes.txt","content":"hello"},
          {"filename":"doc.pdf","content":"data:application/pdf;base64,{{Convert.ToBase64String(PdfBytes)}}"},
          {"fileName":"marked.bin","encoding":"base64","content":"{{Convert.ToBase64String(new byte[] { 0x00, 0xFF })}}"}
        ]}
        """;
        var artifacts = EmbeddedArtifacts.Extract(json);
        Assert.Equal(3, artifacts.Count);
        Assert.Equal("hello", artifacts[0].Content);
        Assert.True(artifacts[1].IsBinary);
        Assert.Equal(PdfBytes, artifacts[1].GetBytes());
        Assert.True(artifacts[2].IsBinary);
        Assert.Equal(new byte[] { 0x00, 0xFF }, artifacts[2].GetBytes());
    }

    [Fact]
    public void Files_key_works_as_an_alias()
    {
        var json = """{"files":[{"name":"out.txt","text":"t"}]}""";
        Assert.Equal("out.txt", Assert.Single(EmbeddedArtifacts.Extract(json)).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]                                     // root must be an object
    [InlineData("""{"artifacts":"nope"}""")]                    // array required
    [InlineData("""{"artifacts":[{"content":"orphan"}]}""")]    // no name
    [InlineData("""{"artifacts":[{"filename":"x.bin","base64":"!!not-base64!!"}]}""")] // explicit base64 must decode
    public void Unusable_payloads_extract_nothing(string? json)
        => Assert.Empty(EmbeddedArtifacts.Extract(json));

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../../b.txt")]
    public void Unsafe_names_are_dropped(string name)
    {
        var json = $$"""{"artifacts":[{"filename":"{{name}}","content":"x"}]}""";
        // Absolute paths are made relative when harmless; traversal segments are dropped entirely.
        var extracted = EmbeddedArtifacts.Extract(json);
        Assert.DoesNotContain(extracted, a => a.Name.Contains("..") || a.Name.StartsWith("/"));
    }

    [Fact]
    public void Nested_relative_paths_survive_sanitization()
    {
        var json = """{"artifacts":[{"filename":"reports/2026/summary.txt","content":"x"}]}""";
        Assert.Equal("reports/2026/summary.txt", Assert.Single(EmbeddedArtifacts.Extract(json)).Name);
    }
}
