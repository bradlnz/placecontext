using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PlaceContext.Application.Access;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;

namespace PlaceContext.Application.Build;

public sealed class BuildChatAgent(IBuildChatStore store, IBuildModelGateway model, PlaceContextService service,
    IPermissionService permissions, ICurrentTenant tenant, IOutcomeReviewer? reviewer = null, OperationCenter? operations = null, ICurrentUser? user = null)
{
    public async Task QueueAsync(BuildConversation conversation, string text, CancellationToken ct = default)
    {
        if (text.Length > 16000 || string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Enter a message of up to 16,000 characters.");
        }
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        var pending = JsonSerializer.Deserialize<BuildConversation>(JsonSerializer.Serialize(conversation))!;
        var message = new JsonObject { ["role"] = "user", ["content"] = content };
        await store.QueueAsync(pending, message, ct);
        conversation.Messages = pending.Messages;
        conversation.Title = pending.Title;
        conversation.Status = pending.Status;
        conversation.Error = null;
        conversation.StopRequested = false;
        Track(conversation);
    }

    public async Task RunAsync(BuildConversation conversation, CancellationToken ct)
    {
        if (conversation.Status != BuildRunStatus.Queued)
        {
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(BuildExecutionLimits.Turn);
        var lease = await store.AcquireAsync(conversation, ct);
        using var pulseStop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        string? pulseError = null;
        var pulse = MaintainPulseAsync();
        var operation = user is null ? null : operations?.ListForTenant(tenant.TenantId, user.UserId)
            .FirstOrDefault(op => op.CorrelationKey == $"build:{conversation.Id}" && op.Status == PortalOperationStatus.Queued)
            ?? Track(conversation);
        if (operation is not null)
        {
            operations!.MarkRunning(operation.Id);
        }
        try
        {
            if (conversation.StopRequested)
            {
                throw new OperationCanceledException();
            }
            RepairInterruptedCalls(conversation.Messages);
            var tools = new BuildTools(conversation.ProjectId, service, permissions, store, reviewer);
            var functions = tools.Functions().ToDictionary(x => x.Name);
            var context = await tools.project_context(deadline.Token);
            var toolSchema = new JsonArray(functions.Values.Select(f => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = f.Name,
                    ["description"] = f.Description,
                    ["parameters"] = JsonNode.Parse(f.JsonSchema.GetRawText())
                }
            }).ToArray());
            for (var step = 0; step < 20; step++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var memory = await store.GetMemoryAsync(conversation.ProjectId, deadline.Token);
                var messages = new JsonArray(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = PromptText.Render("Build.Prompts.ProjectAssistant",
                        ("projectId", conversation.ProjectId.ToString()),
                        ("projectContext", JsonSerializer.Serialize(context)),
                        ("utcNow", DateTimeOffset.UtcNow.ToString("O")),
                        ("timeZone", tenant.TimeZoneId),
                        ("memory", JsonSerializer.Serialize(memory)))
                });
                foreach (var message in conversation.Messages)
                {
                    var history = message!.DeepClone().AsObject();
                    history.Remove("suggested_answers");
                    history.Remove("mcp_connections");
                    messages.Add(history);
                }
                var reply = await model.CompleteAsync(conversation.ConnectionId, conversation.Model, messages, toolSchema, deadline.Token);
                reply["role"] = "assistant";
                reply.Remove("suggested_answers");
                if (conversation.Messages.ToJsonString().Length + reply.ToJsonString().Length > 14_000_000)
                {
                    throw new InvalidOperationException("The conversation is full. Start a new chat to continue.");
                }
                if (reply["tool_calls"] is not JsonArray { Count: > 0 } calls)
                {
                    if (string.IsNullOrWhiteSpace(reply["content"]?.GetValue<string>()))
                    {
                        throw new InvalidOperationException("The model returned no answer.");
                    }
                    conversation.Messages.Add(reply);
                    return;
                }
                if (calls.Count > 8 || calls.Any(call => string.IsNullOrWhiteSpace(call?["id"]?.GetValue<string>()))
                    || calls.Select(call => call!["id"]!.GetValue<string>()).Distinct().Count() != calls.Count)
                {
                    throw new InvalidOperationException("The model returned invalid tool calls.");
                }
                if (calls.Count > 1 && calls.Any(call => call?["function"]?["name"]?.GetValue<string>() == "ask_user"))
                {
                    throw new InvalidOperationException("The model must ask a question separately from other actions. Please retry.");
                }
                conversation.Messages.Add(reply);
                await store.SaveAsync(conversation, lease, false, deadline.Token);
                if (operation is not null)
                {
                    operations!.ReportProgress(operation.Id);
                }
                foreach (var call in calls)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    var name = call!["function"]!["name"]!.GetValue<string>();
                    string result;
                    JsonObject? question = null;
                    try
                    {
                        if (!functions.TryGetValue(name, out var function))
                        {
                            throw new ArgumentException("Unknown build tool.");
                        }
                        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(call["function"]!["arguments"]!.GetValue<string>())
                                                    ?? throw new ArgumentException("Tool arguments are required.");
                        var value = await function.InvokeAsync(new AIFunctionArguments(args), deadline.Token);
                        result = JsonSerializer.Serialize(value);
                        if (name == "ask_user")
                        {
                            question = JsonNode.Parse(result)!.AsObject();
                        }
                        if (result.Length > 100_000)
                        {
                            result = result[..100_000] + "\n[Result truncated; inspect a specific resource for more detail.]";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result = JsonSerializer.Serialize(new
                        {
                            error = ex.Message
                        });
                    }
                    conversation.Messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.DeepClone(), ["content"] = result });
                    await store.SaveAsync(conversation, lease, false, deadline.Token);
                    if (operation is not null)
                    {
                        operations!.ReportProgress(operation.Id);
                    }
                    if (question is not null)
                    {
                        conversation.Messages.Add(new JsonObject
                        {
                            ["role"] = "assistant",
                            ["content"] = question["question"]!.DeepClone(),
                            ["suggested_answers"] = question["answers"]!.DeepClone()
                        });
                        return;
                    }
                }
            }
            conversation.Status = BuildRunStatus.Stopped;
            conversation.Error = "Reached the 20-step limit. Review the completed actions above and send another message to continue.";
        }
        catch (OperationCanceledException)
        {
            var stopped = ct.IsCancellationRequested || conversation.StopRequested;
            conversation.Status = stopped || deadline.IsCancellationRequested ? BuildRunStatus.Stopped : BuildRunStatus.Failed;
            conversation.Error = pulseError is not null
                ? "Build lost its keep-alive connection: " + pulseError + " Completed actions remain applied."
                : stopped
                ? "Build was stopped by the user or its background worker. Completed actions remain applied."
                : deadline.IsCancellationRequested
                    ? $"Build reached its {BuildExecutionLimits.Turn.TotalMinutes:0}-minute run limit. Completed actions remain applied; send another message to continue."
                    : "The model or tool request was interrupted without a Build stop request. Completed actions remain applied; check the provider connection before retrying.";
            RepairInterruptedCalls(conversation.Messages);
        }
        catch (Exception ex)
        {
            conversation.Status = BuildRunStatus.Failed;
            conversation.Error = ex.Message;
            RepairInterruptedCalls(conversation.Messages);
        }
        finally
        {
            await pulseStop.CancelAsync();
            await pulse;
            if (conversation.Error is not null)
            {
                conversation.Messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = conversation.Error });
            }
            await store.SaveAsync(conversation, lease, true, CancellationToken.None);
            if (operation is not null)
            {
                void CompleteOperation()
                {
                    if (conversation.Error is { } error)
                    {
                        operations!.MarkFailed(operation.Id, error);
                        return;
                    }
                    operations!.MarkDone(operation.Id, "Assistant completed.");
                }
                CompleteOperation();
            }
        }

        async Task MaintainPulseAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(BuildExecutionLimits.Pulse);
                while (await timer.WaitForNextTickAsync(pulseStop.Token))
                {
                    using var renewal = CancellationTokenSource.CreateLinkedTokenSource(pulseStop.Token);
                    renewal.CancelAfter(BuildExecutionLimits.Pulse);
                    if (!await store.RenewAsync(conversation.ProjectId, conversation.Id, lease, renewal.Token))
                    {
                        throw new InvalidOperationException("The run was stopped or its lease expired.");
                    }
                }
            }
            catch (OperationCanceledException) when (pulseStop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                pulseError = ex.Message;
                await deadline.CancelAsync();
            }
        }
    }

    private PortalOperation? Track(BuildConversation conversation) => user is null ? null : operations?.Track(
        new TenantInfo(tenant.TenantId, tenant.Slug, tenant.Slug, tenant.TimeZoneId), conversation.ProjectId,
        $"Assistant — {conversation.Title}", $"/project/{conversation.ProjectId}/dashboard?conversation={conversation.Id}",
        $"build:{conversation.Id}", user.UserId);

    public static void RepairInterruptedCalls(JsonArray messages)
    {
        var completed = messages.Where(x => x?["role"]?.GetValue<string>() == "tool").Select(x => x!["tool_call_id"]!.GetValue<string>()).ToHashSet();
        var pending = messages.SelectMany(x => x?["tool_calls"] as JsonArray ?? []).Where(x => !completed.Contains(x!["id"]!.GetValue<string>())).ToArray();
        foreach (var call in pending)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = call!["id"]!.DeepClone(),
                ["content"] = PromptText.Read("Build.Prompts.InterruptedAction")
            });
        }
    }
}
