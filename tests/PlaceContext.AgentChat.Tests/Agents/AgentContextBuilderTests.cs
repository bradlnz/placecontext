using PlaceContext.Application.Features;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class AgentContextBuilderTests
{
    [Fact]
    public async Task BuildContext_returns_context_from_workspace_service()
    {
        var workspace = new FakeAgentChatWorkspaceClient
        {
            ContextToReturn = "## Related project content\nAuth module uses JWT tokens.",
        };
        var builder = new AgentContextBuilder(workspace);

        var context = await builder.BuildContextAsync(
            Guid.NewGuid(), "how does auth work?", maxChunks: 5);

        Assert.Equal(workspace.ContextToReturn, context);
    }

    [Fact]
    public async Task BuildContext_forwards_request_to_workspace_service()
    {
        var projectId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var workspace = new FakeAgentChatWorkspaceClient();
        var builder = new AgentContextBuilder(workspace);

        await builder.BuildContextAsync(
            projectId, "Tell me about 20 Balfour Street, Darra", 7, cancellation.Token);

        var call = Assert.IsType<
            ValueTuple<Guid, string, int, CancellationToken>>(workspace.LastBuildContextCall);
        Assert.Equal(projectId, call.Item1);
        Assert.Equal("Tell me about 20 Balfour Street, Darra", call.Item2);
        Assert.Equal(7, call.Item3);
        Assert.Equal(cancellation.Token, call.Item4);
    }

    [Theory]
    [InlineData("Tell me about 20 Balfour Street, Darra", "20 Balfour Street")]
    [InlineData("what do we know about 123A Old Windsor Road?", "123A Old Windsor Road")]
    [InlineData("summarise 5 McGregor Crescent and 9 McGregor Crescent", "5 McGregor Crescent")]
    public void ExtractMentionTerms_finds_street_addresses(string message, string expected)
        => Assert.Contains(expected, AgentContextBuilder.ExtractMentionTerms(message));

    [Fact]
    public void ExtractMentionTerms_finds_quoted_names()
        => Assert.Contains(
            "Balfour feasibility study",
            AgentContextBuilder.ExtractMentionTerms("read \"Balfour feasibility study\" please"));

    [Fact]
    public void ExtractMentionTerms_ignores_plain_questions()
        => Assert.Empty(AgentContextBuilder.ExtractMentionTerms(
            "how does the feasibility matrix work?"));
}
