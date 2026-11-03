using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Build;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Workload;

public sealed class ModelChainQuestionGenerator(IHttpClientFactory clients, IOptions<VirtualMachineEnvironmentOptions> options)
    : IChainQuestionGenerator
{
    private static readonly JsonArray Tools = JsonNode.Parse("""
        [{"type":"function","function":{"name":"ask_question","description":"Return the validation question for the reviewer.","parameters":{"type":"object","properties":{"question":{"type":"string","maxLength":4000}},"required":["question"],"additionalProperties":false}}}]
        """)!.AsArray();

    public async Task<string> GenerateAsync(LoopChainAction action, string? output, CancellationToken ct)
    {
        var model = options.Value.Agent;
        // A human can review the supplied instructions without a question-writing model.
        if (action.Type == LoopChainAction.HumanType && string.IsNullOrWhiteSpace(model.Model))
        {
            return action.Instructions;
        }
        if (string.IsNullOrWhiteSpace(model.Model) || model.Provider is not ("codex" or "chat-completions"))
        {
            throw new InvalidOperationException("Configure the workflow agent model (PlaceContext:Qemu:Agent) to generate validation questions.");
        }
        if (model.Provider == "chat-completions" && (!Uri.TryCreate(model.Endpoint, UriKind.Absolute, out var endpoint)
                    || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length > 0))
        {
            throw new InvalidOperationException("Configure the workflow agent's chat-completions endpoint.");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var context = output ?? "{}";
        if (context.Length > 65_536)
        {
            context = context[..65_536] + "\n[Remaining output omitted]";
        }
        var audience = action.Type == LoopChainAction.HumanType ? "human user" : "external reviewing agent";
        var format = action.ResponseType == LoopChainAction.YesNoResponse || action.AgentProvider == LoopChainAction.JevAgent
            ? PromptText.Read("Workflows.Prompts.YesNoResponse")
            : PromptText.Read("Workflows.Prompts.WrittenResponse");
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = PromptText.Render("Workflows.Prompts.ValidationQuestion",
                        ("audience", audience),
                        ("responseFormat", format)) },
            new JsonObject { ["role"] = "user", ["content"] = new JsonObject
                { ["reviewInstructions"] = action.Instructions, ["workflowOutput"] = context }.ToJsonString() },
        };
        JsonObject message;
        async Task<JsonObject> GenerateMessageAsync()
        {
            JsonObject resolvedMessage;
            if (model.Provider == "codex")
            {
                await using var session = new CliAgentSession(model.Model);
                resolvedMessage = await session.NextAsync(messages, Tools, deadline.Token);
                return resolvedMessage;
            }
            {
                using var client = clients.CreateClient("qemu-agent");
                using var request = new HttpRequestMessage(HttpMethod.Post, model.Endpoint)
                {
                    Content = JsonContent.Create(new
                    {
                        model = model.Model,
                        messages,
                        tools = Tools,
                        tool_choice = new
                        {
                            type = "function",
                            function = new
                            {
                                name = "ask_question"
                            }
                        },
                        parallel_tool_calls = false,
                        max_tokens = 1200
                    }),
                };
                if (!string.IsNullOrWhiteSpace(model.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Validation question model returned HTTP {(int)response.StatusCode}. Check the workflow agent configuration.");
                }
                await response.Content.LoadIntoBufferAsync(65_536, deadline.Token);
                var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
                resolvedMessage = body?["choices"]?[0]?["message"]?.AsObject()
                    ?? throw new InvalidOperationException("The model returned no validation question.");
                return resolvedMessage;
            }
        }
        message = await GenerateMessageAsync();
        if (message["tool_calls"] is not JsonArray { Count: 1 } calls
                    || calls[0]?["function"]?["name"]?.GetValue<string>() != "ask_question")
        {
            throw new InvalidOperationException("The model must return one validation question.");
        }
        var question = JsonNode.Parse(calls[0]!["function"]!["arguments"]!.GetValue<string>())?["question"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000)
        {
            throw new InvalidOperationException("The model returned an empty or oversized validation question.");
        }
        return question;
    }
}
