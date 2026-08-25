using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ChatChannelTests
{
    [Theory]
    [InlineData("Team Updates", "team-updates")]
    [InlineData("  Launch / Planning  ", "launch-planning")]
    [InlineData("---Support---", "support")]
    public void NormalizeName_produces_slack_style_channel_names(string input, string expected)
    {
        Assert.Equal(expected, ChatChannel.NormalizeName(input));
    }

    [Fact]
    public void Create_builds_an_empty_project_channel()
    {
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-25T10:00:00+10:00");

        var channel = ChatChannel.Create(projectId, "Team Updates", now);

        Assert.Equal(projectId, channel.ProjectId);
        Assert.Equal("team-updates", channel.Title);
        Assert.Empty(channel.Messages);
        Assert.Equal(now, channel.CreatedAt);
        Assert.Equal(now, channel.LastMessageAt);
    }

    [Fact]
    public void Create_rejects_a_name_without_letters_or_numbers()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ChatChannel.Create(Guid.NewGuid(), "---", DateTimeOffset.UtcNow));

        Assert.Contains("channel name", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
