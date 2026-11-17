using System.Text.Json;
using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Application.Build;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ContextAssistantViewModelTests
{
    [Fact]
    public async Task Question_choices_send_once_and_do_not_replace_a_typed_answer()
    {
        var gateway = new Gateway { Reply = "{\"question\":\"Which language?\",\"answers\":[\"Use Python\",\"Use C#\"]}" };
        using var vm = new ContextAssistantViewModel(new Connections(), gateway);
        await vm.LoadAsync();
        vm.Draft = "Help me write code";
        await vm.SendAsync("code", () => Task.FromResult("file"));
        var question = vm.Messages.Last();
        Assert.Equal("Which language?", question.Text);
        Assert.Equal(["Use Python", "Use C#"], question.Answers);
        Assert.Null(vm.Suggestion);
        vm.Draft = "Use Rust";
        await vm.AnswerAsync(question, "Use Python", "code", () => Task.FromResult("file"));
        Assert.Equal("Use Rust", vm.Draft);
        Assert.Equal(2, vm.Messages.Count);
        vm.Draft = "";
        await vm.AnswerAsync(question, "Unlisted", "code", () => Task.FromResult("file"));
        Assert.Equal(2, vm.Messages.Count);
        await vm.AnswerAsync(question, "Use Python", "code", () => Task.FromResult("file"));
        Assert.Equal("Use Python", vm.Messages[2].Text);
        await vm.AnswerAsync(question, "Use C#", "code", () => Task.FromResult("file"));
        Assert.Equal(4, vm.Messages.Count);
    }

    [Theory]
    [InlineData("{\"question\":\"Choose?\",\"answers\":[\"Python\",\"python\"]}")]
    [InlineData("{\"question\":\"Choose?\",\"answers\":[\"Python\",null]}")]
    [InlineData("{\"question\":\"Choose?\",\"answers\":[\"Python\"]}")]
    [InlineData("{\"question\":true,\"answers\":[\"Python\",\"C#\"]}")]
    [InlineData("A normal Markdown response")]
    public void Invalid_question_payloads_remain_readable_without_buttons(string text)
    {
        var reply = ContextAssistantViewModel.ParseReply(text);
        Assert.Equal(text, reply.Text);
        Assert.Empty(reply.Answers);
    }

    [Fact]
    public void Fenced_question_json_is_rendered_as_a_question()
    {
        var reply = ContextAssistantViewModel.ParseReply("```json\n{\"question\":\"Choose?\",\"answers\":[\"Python\",\"C#\"]}\n```");
        Assert.Equal("Choose?", reply.Text);
        Assert.Equal(2, reply.Answers.Count);
    }

    [Fact]
    public async Task Switching_pages_cancels_the_old_response_and_clears_its_draft()
    {
        var gateway = new Gateway { Wait = true };
        using var vm = new ContextAssistantViewModel(new Connections(), gateway);
        await vm.LoadAsync();
        vm.Draft = "Analyze this page";
        var request = vm.SendAsync("data", () => Task.FromResult("old page"));
        vm.ResetContext();
        await request;
        Assert.Empty(vm.Messages);
        Assert.Equal("", vm.Draft);
        Assert.Null(vm.Suggestion);
        Assert.Null(vm.Notice);
        Assert.Null(vm.Error);
        Assert.False(vm.Busy);
    }

    [Fact]
    public void Reply_parts_keep_prose_and_render_multiple_code_blocks_with_their_languages()
    {
        var parts = ContextAssistantViewModel.ReplyParts("Before\n\n```sql\nSELECT 1;\n```\n\nBetween\n\n```py\nprint(1)\n```\n\nAfter", "workspace");
        var code = parts.Where(part => part.Language is not null).ToArray();
        Assert.Equal(["sql", "python"], code.Select(part => part.Language));
        Assert.Equal(["SELECT 1;", "print(1)"], code.Select(part => part.Text));
        var prose = string.Concat(parts.Where(part => part.Language is null).Select(part => part.Text));
        Assert.Contains("Before", prose);
        Assert.Contains("Between", prose);
        Assert.Contains("After", prose);
        Assert.DoesNotContain("```", prose);
        Assert.DoesNotContain("SELECT 1", prose);
    }

    [Theory]
    [InlineData("data", "sql")]
    [InlineData("observability", "logquery")]
    [InlineData("code", "plaintext")]
    public void Unlabelled_code_uses_the_current_editor_language(string kind, string language) =>
        Assert.Equal(language, Assert.Single(ContextAssistantViewModel.ReplyParts("```\ncontent\n```", kind)).Language);

    [Fact]
    public async Task Sends_current_context_and_history_then_applies_only_on_request()
    {
        var gateway = new Gateway();
        using var vm = new ContextAssistantViewModel(new Connections(), gateway);
        await vm.LoadAsync();
        Assert.Single(vm.Connections);
        vm.Draft = "Write a query";
        await vm.SendAsync("data", () => Task.FromResult("schema: readings(value numeric)"));
        Assert.Null(vm.Error);
        Assert.Equal(2, vm.Messages.Count);
        Assert.Equal("select count(*) from readings;", vm.Suggestion);
        using var first = JsonDocument.Parse(gateway.Input!);
        Assert.Contains("readings", first.RootElement.GetProperty("context").GetString());
        Assert.Equal(0, first.RootElement.GetProperty("history").GetArrayLength());

        string? applied = null;
        await vm.ApplyAsync(code => { applied = code; return Task.CompletedTask; });
        Assert.Equal("select count(*) from readings;", applied);
        Assert.Null(vm.Suggestion);
        vm.Draft = "Explain it";
        await vm.SendAsync("data", () => Task.FromResult("updated schema"));
        using var second = JsonDocument.Parse(gateway.Input!);
        Assert.Equal("updated schema", second.RootElement.GetProperty("context").GetString());
        Assert.Equal(2, second.RootElement.GetProperty("history").GetArrayLength());
        vm.Clear();
        Assert.Empty(vm.Messages);
        Assert.Null(vm.Suggestion);
    }

    [Fact]
    public async Task Failure_and_cancellation_preserve_the_question_for_retry()
    {
        var gateway = new Gateway { Fail = true };
        using var vm = new ContextAssistantViewModel(new Connections(), gateway);
        await vm.LoadAsync();
        vm.Draft = "Analyze retries";
        await vm.SendAsync("observability", () => Task.FromResult("loaded runs"));
        Assert.Equal("Provider unavailable", vm.Error);
        Assert.Empty(vm.Messages);
        Assert.Equal("Analyze retries", vm.Draft);
        gateway.Fail = false;
        gateway.Wait = true;
        var pending = vm.SendAsync("observability", () => Task.FromResult("new snapshot"));
        Assert.True(vm.Busy);
        vm.Stop();
        await pending;
        Assert.False(vm.Busy);
        Assert.Empty(vm.Messages);
        Assert.Equal("Analyze retries", vm.Draft);
        Assert.Contains("stopped", vm.Notice);
    }

    [Fact]
    public async Task Oversized_context_never_reaches_the_provider_and_failed_apply_keeps_the_draft()
    {
        var gateway = new Gateway();
        using var vm = new ContextAssistantViewModel(new Connections(), gateway);
        await vm.LoadAsync();
        vm.Draft = "Write code";
        await vm.SendAsync("code", () => Task.FromResult(new string('x', 100001)));
        Assert.Null(gateway.Input);
        Assert.NotNull(vm.Error);
        await vm.SendAsync("code", () => Task.FromResult("complete active file"));
        await vm.ApplyAsync(_ => throw new InvalidOperationException("File changed"));
        Assert.Equal("File changed", vm.Error);
        Assert.NotNull(vm.Suggestion);
    }

    [Theory]
    [InlineData("A plain answer", null)]
    [InlineData("```sql\nselect 1;\n```", "select 1;")]
    [InlineData("```python\nprint(1)\n```\n```python\nprint(2)\n```", null)]
    [InlineData("```\n\n```", null)]
    public void Only_one_nonempty_code_block_is_an_applicable_draft(string text, string? expected) =>
        Assert.Equal(expected, ContextAssistantViewModel.SingleCodeBlock(text));

    [Theory]
    [InlineData("code", "language/runtime")]
    [InlineData("observability", "sampling limits")]
    [InlineData("data", "read-only SELECT")]
    public void Instructions_match_each_editor_and_do_not_authorize_execution(string kind, string expected)
    {
        var instructions = ContextAssistantViewModel.Instructions(kind);
        Assert.Contains(expected, instructions);
        Assert.Contains("cannot save, execute, deploy", instructions);
    }

    private sealed class Gateway : IAgentModelGateway
    {
        public bool Fail, Wait;
        public string? Input;
        public string Reply = "Here is the query.\n```sql\nselect count(*) from readings;\n```";
        public async Task<AgentCompletion> CompleteMeteredAsync(Guid connectionId, string model, string instruction, string input, CancellationToken ct)
        {
            Input = input;
            if (Fail) throw new InvalidOperationException("Provider unavailable");
            if (Wait) await Task.Delay(Timeout.Infinite, ct);
            return new(Reply, model, null, null);
        }
    }

    private sealed class Connections : IModelConnections
    {
        public Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModelConnection>>([
                new(Guid.NewGuid(), "Test", "openai", "http://localhost", ["test-model"], true, false),
                new(Guid.NewGuid(), "Disabled", "openai", "http://localhost", ["test-model"], false, false)]);
        public Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
