using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ReviewedVirtualMachineTaskAgentTests
{
    private static ReviewedVirtualMachineTaskAgent Agent(IHttpClientFactory api, VirtualMachineEnvironmentOptions? config = null) => new(api,
        Options.Create(config ?? new()
        {
            Agent = new()
            {
                Provider = "chat-completions",
                Model = "vision",
                Endpoint = "http://vision.test/completions"
            }
        }), Options.Create(new ModelReviewOptions { ApiKey = "host-secret" }));
    private static Task<byte[]> LiveFrame(CancellationToken ct) => Task.FromResult(Convert.FromBase64String(Screen));
    private const string Prompt = "Start at https://orders.example.test and verify the order.";
    private static string Screen
    {
        get
        {
            var png = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 1280);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 800);
            return Convert.ToBase64String(png);
        }
    }
    private const string Vision = """{"id":1,"kind":"vision","body":{"state":{"task":"Inspect"}}}""";
    private const string Judge = """{"id":2,"kind":"jev","body":{"state":{"screenshot":"screen-image","proposal":{"description":"Click order","action":"click","x":640,"y":400}},"questions":{"action":{"type":"choice"}}}}""";
    private const string Done = """{"status":"goal_achieved","page":"Order ready","steps":[{"detail":"Verified order","goal_done":0.99}]}""";

    [Fact]
    public void Codex_vision_accepts_plain_text_and_rejects_empty_oversized_or_tool_responses()
    {
        var response = ReviewedVirtualMachineTaskAgent.CodexVisionResponse(new JsonObject { ["content"] = "4101" });
        Assert.Equal("4101", response["choices"]![0]!["message"]!["content"]!.GetValue<string>());
        foreach (var text in new[] { null, "", " ", new string('x', 16385) })
        {
            Assert.Throws<InvalidOperationException>(() => ReviewedVirtualMachineTaskAgent.CodexVisionResponse(new JsonObject { ["content"] = text }));
        }
        Assert.Throws<InvalidOperationException>(() => ReviewedVirtualMachineTaskAgent.CodexVisionResponse(
                    new JsonObject { ["content"] = "4101", ["tool_calls"] = new JsonArray() }));
    }

    [Fact]
    public async Task Vision_failure_identifies_the_stage_without_exposing_request_data()
    {
        using var api = new ModelReviewTestApi((_, _) => throw new Exception("Must not call"));
        var result = await Agent(api).RunAsync(Prompt, null, (_, limit, _) => Task.FromResult(new VirtualMachineEnvironmentResult(0,
            limit == 131072 ? """{"id":1,"kind":"vision","body":{"private":"private field data"}}""" : "", "")), default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Browser verification visual proposal failed", result.Stderr);
        Assert.DoesNotContain("private", result.Stderr);
    }

    [Theory]
    [InlineData("Start at https://example.test/path. Read the page", "https://example.test/path")]
    [InlineData("Open http://127.0.0.1:8080/orders", "http://127.0.0.1:8080/orders")]
    public void Starting_URL_is_read_from_task(string prompt, string expected) => Assert.Equal(expected, ReviewedVirtualMachineTaskAgent.StartUrl(prompt));

    [Theory]
    [InlineData("http://localhost:7700/orders", "http://10.0.2.2:7700/orders")]
    [InlineData("http://127.0.0.1:8080/orders", "http://127.0.0.1:8080/orders")]
    [InlineData("https://orders.example.test/orders", "https://orders.example.test/orders")]
    public void Host_local_urls_are_mapped_to_the_QEMU_gateway(string url, string expected)
        => Assert.Equal(expected, ReviewedVirtualMachineTaskAgent.GuestStartUrl(url));

    [Theory]
    [InlineData("Run echo hello")]
    [InlineData("Open file:///etc/passwd")]
    [InlineData("Visit https://user:password@example.test")]
    public void Missing_or_credential_URL_is_rejected(string prompt) => Assert.Throws<InvalidOperationException>(() => ReviewedVirtualMachineTaskAgent.StartUrl(prompt));

    [Fact]
    public async Task Host_calls_Jev_and_keeps_credentials_out_of_guest_with_replay()
    {
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "vision.test")
            {
                var vision = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("image_url", vision);
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[]}")
                };
            }
            Assert.Equal("https://api.typesafe.ai/v1/systemone", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer host-secret", request.Headers.Authorization!.ToString());
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.Equal("jev-1.14.0", body["model"]!.GetValue<string>());
            Assert.Equal("screen-image", body["state"]!["screenshot"]!.GetValue<string>());
            Assert.Equal(Screen, body["assets"]!["screen-image"]!["data_base64"]!.GetValue<string>());
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("TypeSafe-Danger-Zone-Images")));
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"model":"jev-test","answers":{"action":{"choice":"act","confidence":0.74}},"usage":{"input_tokens":1,"output_tokens":1}}""")
            };
        });
        var pending = new Queue<string>([Vision, Judge, Done]);
        var commands = new List<string>();
        var frames = new List<VirtualMachineReplayCapture>();
        var result = await Agent(api).RunAsync(Prompt, null, (command, limit, _) =>
        {
            commands.Add(command);
            return Task.FromResult(new VirtualMachineEnvironmentResult(0, limit == 131072 ? pending.Dequeue()
                : "", ""));
        }, default, (capture, _) =>
                {
                    frames.Add(capture);
                    return Task.CompletedTask;
                }, readLiveFrame: LiveFrame);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, api.Requests);
        Assert.Equal(.5, frames[1].Frame.PointerX);
        Assert.True(frames[1].Frame.Click);
        Assert.Equal(3, frames.Count);
        Assert.Contains(commands, command => command.Contains("chmod 700") && command.Contains("/pc-jev"));
        Assert.DoesNotContain(commands, command => command.Contains("host-secret") || command.Contains(Convert.ToBase64String("host-secret"u8.ToArray())));
        Assert.DoesNotContain(commands, command => command.Contains("screen.b64") || command.Contains("scrot"));
        Assert.DoesNotContain(commands, command => command.Contains("npm ") || command.Contains("node ") || command.Contains("python3"));
        Assert.Equal("Verified order", Assert.Single(result.Activity!).Description);
    }

    [Theory]
    [InlineData("done", 0.1, 1)]
    [InlineData("done", 0.70, 1)]
    [InlineData("goal_achieved", 0.71, 0)]
    [InlineData("goal_achieved", 0.99, 0)]
    [InlineData("max_steps", 0.99, 1)]
    [InlineData("done", 1.01, 1)]
    public void Completion_requires_goal_evidence_and_a_success_status(string status, double goal, int exitCode)
    {
        var result = JsonSerializer.SerializeToNode(new
        {
            status,
            steps = new[] { new { goal_done = goal } }
        })!.AsObject();
        Assert.Equal(exitCode, ReviewedVirtualMachineTaskAgent.Complete(result, 2).ExitCode);
    }

    [Fact]
    public async Task HTTP_errors_are_sanitized_and_cancellation_propagates()
    {
        using var denied = new ModelReviewTestApi((request, _) => Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host == "vision.test" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(request.RequestUri!.Host == "vision.test" ? "{\"choices\":[]}" : "sensitive-provider-body")
        }));
        Queue<string> pending = new([Vision, Judge]);
        Task<VirtualMachineEnvironmentResult> Guest(string command, int limit, CancellationToken ct) => Task.FromResult(new VirtualMachineEnvironmentResult(0,
            limit == 131072 ? pending.Dequeue() : "", ""));
        var result = await Agent(denied).RunAsync(Prompt, null, Guest, default, readLiveFrame: LiveFrame);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("401", result.Stderr);
        Assert.DoesNotContain("sensitive", result.Stderr);
        pending = new([Vision, Judge]);
        using var cancel = new CancellationTokenSource();
        using var api = new ModelReviewTestApi(async (_, ct) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Agent(api).RunAsync(Prompt, null, Guest, cancel.Token, readLiveFrame: LiveFrame));
    }

    [Fact]
    public async Task Truncated_guest_output_and_invalid_sequence_stop_without_calling_Jev()
    {
        using var api = new ModelReviewTestApi((_, _) => throw new Exception("Must not call"));
        foreach (var truncated in new[] { true, false })
        {
            var result = await Agent(api).RunAsync(Prompt, null, (_, limit, _) => Task.FromResult(new VirtualMachineEnvironmentResult(0,
                limit == 131072 ? """{"id":2,"kind":"jev","body":{}}""" : "", "", OutputTruncated: limit == 131072 && truncated)), default, readLiveFrame: LiveFrame);
            Assert.Equal(1, result.ExitCode);
        }
        Assert.Equal(0, api.Requests);
    }
}
