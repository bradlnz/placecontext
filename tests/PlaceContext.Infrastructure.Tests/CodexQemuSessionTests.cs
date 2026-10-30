using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class CodexQemuSessionTests
{
    [Theory]
    [InlineData("models")]
    [InlineData("signedout")]
    public async Task Discovery_checks_login_and_reads_the_paginated_model_catalog(string mode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = new CodexQemuSession("", Fake(mode));
        var models = await session.ListModelsAsync(timeout.Token);
        Assert.Equal(mode == "models" ? new[] { "preferred", "other", "last" } : [], models);
    }

    [Fact]
    public async Task Completed_text_is_returned_to_chat_callers()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = new CodexQemuSession("test-model", Fake("answer"));
        var response = await session.NextAsync(Messages(), Tools(), timeout.Token);
        Assert.Equal("A real answer.", response["content"]!.GetValue<string>());
    }

    private static ProcessStartInfo Fake(string mode)
    {
        var start = CodexQemuSession.BuildStartInfo(Path.GetTempPath());
        start.FileName = "python3";
        start.ArgumentList.Clear();
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "TestData", "codex-app-server.py"));
        start.ArgumentList.Add(mode);
        return start;
    }
    private static JsonArray Messages() => JsonNode.Parse("""
        [{"role":"system","content":"Guest tools only."},{"role":"user","content":"Check the desktop."},
         {"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,first"}}]}]
        """)!.AsArray();
    private static JsonArray Tools() => JsonNode.Parse("""
        [{"type":"function","function":{"name":"guest_shell","description":"Guest only","parameters":{"type":"object"}}},
         {"type":"function","function":{"name":"live_desktop","description":"Read Live preview","parameters":{"type":"object"}}},
         {"type":"function","function":{"name":"finish","description":"Finish task","parameters":{"type":"object"}}}]
        """)!.AsArray();

    [Fact]
    public async Task Local_protocol_routes_commands_and_live_frames_without_exposing_host_tools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = new CodexQemuSession("test-model", Fake("actions"));
        var messages = Messages();
        var command = await session.NextAsync(messages, Tools(), timeout.Token);
        Assert.Equal("guest_shell", command["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        messages.Add(command);
        messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "HELLO" });
        var frame = await session.NextAsync(messages, Tools(), timeout.Token);
        Assert.Equal("live_desktop", frame["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        messages.Add(frame);
        messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "Live desktop frame follows" });
        messages.Add(JsonNode.Parse("""{"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,second"}}]}"""));
        var finish = await session.NextAsync(messages, Tools(), timeout.Token);
        Assert.Equal("finish", finish["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("approval", "unsupported host operation")]
    [InlineData("failed", "Codex turn failed")]
    public async Task Unsupported_requests_and_failed_turns_stop_without_leaking_provider_details(string mode, string error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = new CodexQemuSession("test-model", Fake(mode));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.NextAsync(Messages(), Tools(), timeout.Token));
        Assert.Contains(error, failure.Message);
        Assert.DoesNotContain("secret upstream details", failure.Message);
    }

    [Fact]
    public async Task Cancellation_interrupts_protocol_and_disposal_removes_the_private_working_directory()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "pc-codex-*").Order().ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await using (var session = new CodexQemuSession("test-model", Fake("cancel")))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.NextAsync(Messages(), Tools(), timeout.Token));
        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "pc-codex-*").Order().ToArray());
    }

    [Fact]
    public void Codex_configuration_requires_a_model_but_no_http_endpoint_or_api_key()
    {
        using var client = new QemuPromptAgentTests.ModelApi();
        var options = new QemuEnvironmentOptions { Agent = new() { Provider = "codex", Model = "test-model" } };
        var agent = new QemuPromptAgent(client, Options.Create(options));
        Assert.Null(agent.ConfigurationError);
        options.Agent.Model = "";
        Assert.NotNull(agent.ConfigurationError);
        options.Agent.Model = "test-model";
        options.Agent.Provider = "unknown";
        Assert.NotNull(agent.ConfigurationError);
    }
}
