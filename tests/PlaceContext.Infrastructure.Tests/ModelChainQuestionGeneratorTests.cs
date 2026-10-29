using Microsoft.Extensions.Options;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ModelChainQuestionGeneratorTests
{
    [Fact]
    public async Task Human_review_without_a_model_uses_instructions_without_calling_a_provider()
    {
        using var api = new QemuPromptAgentTests.ModelApi();
        var generator = new ModelChainQuestionGenerator(api, Options.Create(new QemuEnvironmentOptions()));
        const string instructions = "Review Jev findings and record your approval and rationale.";
        var question = await generator.GenerateAsync(new(LoopChainAction.HumanType, instructions, "review"),
            "{\"jev_evaluation\":{\"status\":\"insufficient_evidence\"}}", default);
        Assert.Equal(instructions, question);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Agent_review_without_a_model_still_requires_configuration()
    {
        using var api = new QemuPromptAgentTests.ModelApi();
        var generator = new ModelChainQuestionGenerator(api, Options.Create(new QemuEnvironmentOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(
            new(LoopChainAction.AgentType, "Verify the output", "review"), "{}", default));
        Assert.Empty(api.Requests);
    }

    [Theory]
    [InlineData(LoopChainAction.HumanType, LoopChainAction.YesNoResponse, "human user")]
    [InlineData(LoopChainAction.AgentType, LoopChainAction.TextResponse, "external reviewing agent")]
    public async Task Question_uses_instructions_and_output_without_answering_or_executing_actions(string kind, string responseType, string audience)
    {
        using var api = new QemuPromptAgentTests.ModelApi(QemuPromptAgentTests.Tool("ask_question", new { question = "Is the reported total of 42 correct?" }));
        var generator = new ModelChainQuestionGenerator(api, Options.Create(QemuPromptAgentTests.OptionsForAgent()));
        var question = await generator.GenerateAsync(new(kind, "Validate the total", "review", responseType), "{\"total\":42}", default);
        Assert.Equal("Is the reported total of 42 correct?", question);
        var request = Assert.Single(api.Requests);
        var system = request["messages"]![0]!["content"]!.GetValue<string>();
        Assert.Contains(audience, system);
        Assert.Contains("untrusted data", system);
        Assert.Contains("Do not answer", system);
        var input = request["messages"]![1]!["content"]!.GetValue<string>();
        Assert.Contains("Validate the total", input);
        Assert.Contains("42", input);
        Assert.DoesNotContain("host-only-secret", input);
        Assert.Single(request["tools"]!.AsArray());
        Assert.Equal("ask_question", request["tool_choice"]!["function"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("finish", "Approved")]
    [InlineData("ask_question", "")]
    public async Task Invalid_model_response_cannot_bypass_review(string tool, string question)
    {
        using var api = new QemuPromptAgentTests.ModelApi(QemuPromptAgentTests.Tool(tool, new { question }));
        var generator = new ModelChainQuestionGenerator(api, Options.Create(QemuPromptAgentTests.OptionsForAgent()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(new(LoopChainAction.HumanType, "Review", "answer"), "{}", default));
    }

    [Fact]
    public async Task Jev_receives_a_yes_no_question_instead_of_a_request_for_prose()
    {
        using var api = new QemuPromptAgentTests.ModelApi(QemuPromptAgentTests.Tool("ask_question", new { question = "Is the total correct?" }));
        var generator = new ModelChainQuestionGenerator(api, Options.Create(QemuPromptAgentTests.OptionsForAgent()));
        await generator.GenerateAsync(new(LoopChainAction.AgentType, "Check total", "review", agentProvider: LoopChainAction.JevAgent), "{}", default);
        var system = Assert.Single(api.Requests)["messages"]![0]!["content"]!.GetValue<string>();
        Assert.Contains("answered Yes or No", system);
        Assert.DoesNotContain("written response", system);
    }
}
