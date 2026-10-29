using System.Diagnostics;
using System.Text.Json.Nodes;
using PlaceContext.Infrastructure.Build;

namespace PlaceContext.Infrastructure.Tests;

public sealed class LocalCliModelsTests
{
    private static JsonArray Tools() => JsonNode.Parse("""[{"function":{"name":"inspect","description":"Inspect a resource","parameters":{"type":"object"}}}]""")!.AsArray();

    [Fact]
    public void Claude_has_no_host_tools_or_customizations_and_uses_structured_output()
    {
        var start = LocalCliModels.ClaudeStartInfo("/tmp", "default");
        var args = start.ArgumentList.ToArray();
        Assert.False(start.UseShellExecute);
        Assert.Equal("", args[Array.IndexOf(args, "--tools") + 1]);
        Assert.Contains("--safe-mode", args);
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Contains("--json-schema", args);
        Assert.Contains("{\"disableAllHooks\":true}", args);
        Assert.DoesNotContain("--model", args);
        Assert.Contains("sonnet", LocalCliModels.ClaudeStartInfo("/tmp", "sonnet").ArgumentList);
    }

    [Fact]
    public void Transcript_preserves_history_and_images_without_mutating_the_stored_messages()
    {
        var messages = JsonNode.Parse("""
            [{"role":"system","content":"Project instructions"},
             {"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,AAAA"}}]},
             {"role":"assistant","tool_calls":[{"id":"prior","function":{"name":"inspect","arguments":"{}"}}]},
             {"role":"tool","tool_call_id":"prior","content":"Already inspected"}]
            """)!.AsArray();
        var before = messages.ToJsonString();
        var input = LocalCliModels.Transcript(messages);
        Assert.Equal("Project instructions", input[0]!["content"]!.GetValue<string>());
        var text = input[1]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("Already inspected", text);
        Assert.Contains("prior", text);
        Assert.DoesNotContain("base64", text);
        Assert.Equal("data:image/png;base64,AAAA", input[1]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal(before, messages.ToJsonString());
    }

    [Fact]
    public void Claude_actions_use_the_existing_application_tool_format()
    {
        var result = JsonNode.Parse("""{"structured_output":{"content":"Checking","tool_calls":[{"name":"inspect","arguments":"{\"id\":1}"}]}}""")!.AsObject();
        var reply = LocalCliModels.ClaudeResponse(result, Tools());
        Assert.Equal("Checking", reply["content"]!.GetValue<string>());
        Assert.Equal("inspect", reply["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("{\"id\":1}", reply["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(reply["tool_calls"]![0]!["id"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("{\"is_error\":true}")]
    [InlineData("{\"result\":\"unstructured answer\"}")]
    [InlineData("{\"structured_output\":{\"content\":\"\",\"tool_calls\":[{\"name\":\"shell\",\"arguments\":\"{}\"}]}}")]
    [InlineData("{\"structured_output\":{\"content\":\"\",\"tool_calls\":[{\"name\":\"inspect\",\"arguments\":\"[]\"}]}}")]
    public void Claude_rejects_errors_missing_output_and_unapproved_actions(string json) =>
        Assert.Throws<InvalidOperationException>(() => LocalCliModels.ClaudeResponse(JsonNode.Parse(json)!.AsObject(), Tools()));

    [Theory]
    [InlineData("cancel")]
    [InlineData("oversize")]
    public async Task Process_runner_stops_on_cancellation_or_oversize_output(string mode)
    {
        var start = new ProcessStartInfo("python3") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(mode == "cancel" ? "import time; time.sleep(30)" : "import time; print('x'*2000001, flush=True); time.sleep(30)");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(mode == "cancel" ? 1 : 10));
        if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LocalCliModels.RunAsync(start, "", timeout.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => LocalCliModels.RunAsync(start, "", timeout.Token));
    }
}
