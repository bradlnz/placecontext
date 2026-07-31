using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Tests;

public sealed class CrmClientArtifactTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Direct_upload_is_owned_by_the_client()
    {
        var value = CrmClientArtifact.CreateUpload(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "proposal.pdf",
            "reports", "crm-clients/proposal.pdf", "application/pdf", 2048, T0);

        Assert.True(value.IsDirectUpload);
        Assert.Null(value.SourceArtifactId);
        Assert.Equal("proposal.pdf", value.Title);
    }

    [Fact]
    public void Automation_artifact_keeps_source_and_chain_run_links()
    {
        var sourceId = Guid.NewGuid();
        var chainRunId = Guid.NewGuid();
        var value = CrmClientArtifact.CreateFromRunArtifact(
            Guid.NewGuid(), Guid.NewGuid(), sourceId, chainRunId, "report.html",
            "reports", "runs/report.html", "text/html", 100, T0);

        Assert.False(value.IsDirectUpload);
        Assert.Equal(sourceId, value.SourceArtifactId);
        Assert.Equal(chainRunId, value.ChainRunId);
    }
}
