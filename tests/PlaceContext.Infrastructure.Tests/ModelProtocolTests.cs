using System.Text.Json.Nodes;
using PlaceContext.Infrastructure.Build;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ModelProtocolTests
{
    [Fact]
    public void Provider_adapters_keep_usage_and_credentials_in_their_own_protocol()
    {
        var anthropic = new AnthropicModelProtocol(); var openai = new OpenAiModelProtocol();
        var usage = JsonNode.Parse("""{"usage":{"input_tokens":10,"output_tokens":3,"cache_creation_input_tokens":20,"cache_read_input_tokens":40}}""")!.AsObject();
        Assert.Equal((70L, 3L), anthropic.Usage(usage));
        Assert.Equal((null, null), openai.Usage(usage));
        Assert.Equal((100L, 50L), openai.Usage(JsonNode.Parse("""{"usage":{"prompt_tokens":100,"completion_tokens":50}}""")!.AsObject()));
        using var a = new HttpRequestMessage(); using var o = new HttpRequestMessage();
        anthropic.Authenticate(a, "a-key"); openai.Authenticate(o, "o-key");
        Assert.Null(a.Headers.Authorization); Assert.Equal("a-key", a.Headers.GetValues("x-api-key").Single());
        Assert.False(o.Headers.Contains("x-api-key")); Assert.Equal("Bearer", o.Headers.Authorization!.Scheme);
        Assert.Throws<ArgumentException>(() => ModelProtocols.Get("unknown"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"usage\":{\"input_tokens\":-1,\"output_tokens\":2}}")]
    [InlineData("{\"usage\":{\"input_tokens\":9223372036854775807,\"output_tokens\":2,\"cache_read_input_tokens\":1}}")]
    public void Invalid_or_unreported_usage_is_not_invented(string json) =>
        Assert.Equal((null, null), new AnthropicModelProtocol().Usage(JsonNode.Parse(json)!.AsObject()));
}
