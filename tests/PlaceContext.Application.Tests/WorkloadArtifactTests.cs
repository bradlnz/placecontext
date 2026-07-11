using System.Text;
using PlaceContext.Application.Ports;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The text/binary discriminator for captured /out files. The whole artifact pipeline persists
/// strings, so a file that is not valid UTF-8 text must ride as base64 (IsBinary) — anything else
/// corrupts PDFs and images between the runner and the portal.
/// </summary>
public class WorkloadArtifactTests
{
    [Fact]
    public void Utf8_text_stays_text()
    {
        var a = WorkloadArtifact.FromBytes("report.csv", Encoding.UTF8.GetBytes("a,b\n1,2\näöü €"));
        Assert.False(a.IsBinary);
        Assert.Equal("a,b\n1,2\näöü €", a.Content);
        Assert.Equal(Encoding.UTF8.GetBytes("a,b\n1,2\näöü €"), a.GetBytes());
    }

    [Fact]
    public void Invalid_utf8_becomes_base64_and_round_trips_byte_exact()
    {
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3 };
        var a = WorkloadArtifact.FromBytes("doc.pdf", pdf);
        Assert.True(a.IsBinary);
        Assert.Equal(Convert.ToBase64String(pdf), a.Content);
        Assert.Equal(pdf, a.GetBytes());
    }

    [Fact]
    public void Valid_utf8_containing_nul_is_treated_as_binary()
    {
        // NUL decodes fine as UTF-8 but is a binary tell — and a NUL-bearing "string" would not
        // survive Postgres jsonb, so it must ride as base64.
        var bytes = new byte[] { (byte)'a', 0x00, (byte)'b' };
        var a = WorkloadArtifact.FromBytes("data.bin", bytes);
        Assert.True(a.IsBinary);
        Assert.Equal(bytes, a.GetBytes());
    }

    [Fact]
    public void Empty_file_is_empty_text()
    {
        var a = WorkloadArtifact.FromBytes("empty.txt", Array.Empty<byte>());
        Assert.False(a.IsBinary);
        Assert.Equal("", a.Content);
        Assert.Empty(a.GetBytes());
    }
}
