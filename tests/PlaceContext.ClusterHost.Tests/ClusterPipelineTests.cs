using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlaceContext.ClusterHost;

namespace PlaceContext.ClusterHost.Tests;

public sealed class ClusterPipelineTests
{
    [Fact]
    public async Task Multi_shard_generation_is_coordinated_in_endpoint_order()
    {
        var handler = new PipelineHandler();
        var pipeline = new ClusterPipeline(
            new StaticHttpClientFactory(handler),
            Options.Create(new ClusterProxyOptions
            {
                Model = "test-model",
                ShardEndpoints = ["http://first", "http://last"],
            }),
            NullLogger<ClusterPipeline>.Instance);
        var request = new ClusterChatRequest
        {
            Messages = [new ClusterMessageDto { Role = "user", Content = "hello" }],
            Temperature = 0,
            MaxTokens = 4,
        };

        var output = new StringBuilder();
        await foreach (var text in pipeline.GenerateStreamAsync(request, CancellationToken.None))
            output.Append(text);

        Assert.Equal(" world", output.ToString());
        Assert.Equal(
            [
                "first/v1/tokenize",
                "first/v1/forward",
                "last/v1/forward",
                "last/v1/decode",
                "first/v1/forward",
                "last/v1/forward",
            ],
            handler.Calls);
        Assert.Equal([10], handler.FirstShardTokenIds[0]);
        Assert.Equal([10, 1], handler.FirstShardTokenIds[1]);
    }

    [Fact]
    public void Greedy_sampling_uses_the_last_sequence_position()
    {
        using var doc = JsonDocument.Parse("[[[50,0,0],[0,2,9]]]");

        var token = ClusterPipeline.SampleLastToken(doc.RootElement, temperature: 0, topP: 1);

        Assert.Equal(2, token);
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class PipelineHandler : HttpMessageHandler
    {
        private int _lastShardCalls;

        public List<string> Calls { get; } = [];
        public List<int[]> FirstShardTokenIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            Calls.Add(call);
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return call switch
            {
                "first/v1/tokenize" => Json("{\"token_ids\":[10],\"eos_token_id\":2}"),
                "first/v1/forward" => ForwardFirst(body),
                "last/v1/forward" => ForwardLast(),
                "last/v1/decode" => Json("{\"text\":\" world\",\"token_id\":1}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private HttpResponseMessage ForwardFirst(string body)
        {
            using var doc = JsonDocument.Parse(body);
            FirstShardTokenIds.Add(doc.RootElement.GetProperty("token_ids").EnumerateArray()
                .Select(token => token.GetInt32()).ToArray());
            return Json("{\"hidden_states\":[[[0.25]]],\"shard\":\"0/2\"}");
        }

        private HttpResponseMessage ForwardLast()
        {
            _lastShardCalls++;
            return _lastShardCalls == 1
                ? Json("{\"hidden_states\":[[[0.5]]],\"logits\":[[[0,9,0]]],\"shard\":\"1/2\"}")
                : Json("{\"hidden_states\":[[[0.5]]],\"logits\":[[[0,0,9]]],\"shard\":\"1/2\"}");
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
    }
}
