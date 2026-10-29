using System.Text.Json.Nodes;
using PlaceContext.Infrastructure.Build;
using Xunit.Abstractions;

namespace PlaceContext.Infrastructure.Tests;

public sealed class LocalCliIntegrationTests(ITestOutputHelper output)
{
    [LocalCliFact]
    public async Task Codex_discovers_models_and_completes_a_permission_checked_tool_roundtrip()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var cli = new LocalCliModels();
        var models = await cli.DiscoverAsync("codex", timeout.Token);
        Assert.NotNull(models);
        Assert.NotEmpty(models);
        output.WriteLine("Codex models: " + string.Join(", ", models));
        var messages = JsonNode.Parse("""
            [{"role":"system","content":"Call inspect exactly once with value CLI_OK. After its result appears in history, answer exactly CLI_OK without further tool calls."},
             {"role":"user","content":"Run the tool roundtrip."}]
            """)!.AsArray();
        var tools = JsonNode.Parse("""
            [{"type":"function","function":{"name":"inspect","description":"Return the given test value","parameters":{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}}}]
            """)!.AsArray();
        var call = await cli.CompleteAsync("codex", models[0], messages, tools, timeout.Token);
        output.WriteLine("First reply: " + call.ToJsonString());
        Assert.IsType<JsonArray>(call["tool_calls"]);
        Assert.Equal("inspect", call["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        messages.Add(call);
        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["tool_calls"]![0]!["id"]!.DeepClone(), ["content"] = "CLI_OK" });
        var answer = await cli.CompleteAsync("codex", models[0], messages, tools, timeout.Token);
        output.WriteLine("Final reply: " + answer.ToJsonString());
        Assert.Equal("CLI_OK", answer["content"]!.GetValue<string>());
        output.WriteLine("Codex tool call and final answer verified.");
        var claudeModels = await cli.DiscoverAsync("claude-code", timeout.Token);
        output.WriteLine(claudeModels is null ? "Claude Code is not installed." : "Claude Code models: " + string.Join(", ", claudeModels));
    }
}
