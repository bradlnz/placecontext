using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class ValueObjectTests
{
    [Fact]
    public void CommitSha_rejects_non_hex_and_lowercases()
    {
        Assert.Throws<ArgumentException>(() => CommitSha.From("nothex!"));
        Assert.Equal("abc1234", CommitSha.From("ABC1234").Value);
    }

    [Fact]
    public void RepoPath_requires_absolute_and_trims_trailing_slash()
    {
        Assert.Throws<ArgumentException>(() => RepoPath.From("relative/path"));
        Assert.Equal("/home/brad/code/demo", RepoPath.From("/home/brad/code/demo/").Value);
        Assert.Equal("demo", RepoPath.From("/home/brad/code/demo").LeafName);
    }

    [Fact]
    public void DebtScore_clamps_and_bands()
    {
        Assert.Equal(1.0, DebtScore.From(5.0).Value);
        Assert.Equal(DebtBand.Low, DebtScore.From(0.1).Band);
        Assert.Equal(DebtBand.Critical, DebtScore.From(0.9).Band);
    }

    [Fact]
    public void Rationale_none_is_not_present()
    {
        Assert.False(Rationale.None.IsPresent);
        Assert.True(Rationale.Of("real").IsPresent);
        Assert.False(Rationale.OrNone("  ").IsPresent);
    }

    [Fact]
    public void TestDelta_activity_detection()
    {
        Assert.False(TestDelta.None.HasTestActivity);
        Assert.True(TestDelta.From(1, 0, 0).HasTestActivity);
    }

    [Fact]
    public void Author_kind_drives_agent_flag()
    {
        Assert.True(Author.Agent("claude").IsAgent);
        Assert.False(Author.Human("brad").IsAgent);
    }
}
