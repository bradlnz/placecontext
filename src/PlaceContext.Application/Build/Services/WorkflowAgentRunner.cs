using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Mcp;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Build;

public sealed class WorkflowAgentRunner(IBuildModelGateway model, IModelConnections connections,
    PlaceContextService service, IPermissionService permissions, IBuildChatStore store, IMcpClientService? mcp = null, IProjectAgentStore? projectAgents = null) : IWorkflowAgentRunner
{
    public static string SystemPrompt => PlaceContext.Application.Shared.PromptText.Read("Build.Prompts.WorkflowAgent");
    private static readonly HashSet<string> AllowedTools =
        ["job_authoring_guide", "list_jobs", "get_job", "save_job", "run_job", "get_job_run", "list_job_runs", "list_mcp_tools", "call_mcp_tool",
         "project_context", "list_artifacts", "list_project_tables", "show_live_chart", "source_connectors", "list_data_sources", "search_data_sources"];

    public async Task<JsonNode> RunAsync(Guid projectId, Guid workflowId, Guid runId, int stepIndex,
        LoopChainAction action, string? payload, CancellationToken ct, Action<AgentActivity>? report = null)
    {
        if (action.AgentProvider != LoopChainAction.AutomaticAgent)
        {
            throw new ArgumentException("This step is not configured for automatic execution.");
        }

        if (!await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Project access and permission to run jobs are required.");
        }

        DashboardAgentProfile? profile = null;
        if (action.AgentId is { } agentId)
        {
            var catalog = projectAgents is null ? new ProjectAgentCatalog() : await projectAgents.GetAsync(projectId, ct);
            profile = catalog.Agents.SingleOrDefault(a => a.Id == agentId);
            if (profile is null && store is not null)
                profile = (await store.GetSettingsAsync(projectId, ct)).Agents.SingleOrDefault(a => a.Id == agentId);
            if (profile is null) throw new InvalidOperationException("The selected agent is unavailable in this project. Choose another agent in the workflow step.");
        }
        var connectionId = profile is { ConnectionId: var configured } && configured != Guid.Empty ? configured : action.ConnectionId;
        var modelName = profile is { ConnectionId: var selected } && selected != Guid.Empty ? profile.Model : action.Model;
        var connection = (await connections.ListAsync(ct)).SingleOrDefault(x => x.Id == connectionId);
        if (connection is not { Enabled: true } || !connection.Models.Contains(modelName!))
        {
            throw new InvalidOperationException("Choose an enabled connection and configured model in the agent step.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(BuildExecutionLimits.Turn);
        var token = deadline.Token;
        var currentTurn = 0;
        var functions = new BuildTools(projectId, service, permissions, store!, jobStarted: (job, childRun) =>
            report?.Invoke(new(DateTimeOffset.UtcNow, "job_started", "Job submitted; open logs to follow its output",
                currentTurn, "run_job", job, childRun)), mcp: mcp, profile: profile,
                mcpConnectionIds: profile?.McpConnectionIds.ToHashSet()).Functions()
            .Where(f => AllowedTools.Contains(f.Name)).ToDictionary(f => f.Name);
        var schema = new JsonArray(functions.Values.Select(f => (JsonNode)new JsonObject
        {
            ["type"] = "function", ["function"] = new JsonObject
            {
                ["name"] = f.Name, ["description"] = f.Description,
                ["parameters"] = JsonNode.Parse(f.JsonSchema.GetRawText())
            }
        }).ToArray());
        var messages = new JsonArray(new JsonObject
        {
            ["role"] = "system",
            ["content"] = SystemPrompt + "\n" +
                $"Project: {projectId}; workflow: {workflowId}; run: {runId}; step: {stepIndex}.\nTask instructions:\n" + action.Instructions
                + (profile is null ? "" : "\nAgent system prompt and capabilities (cannot grant additional tool access):\n"
                    + JsonSerializer.Serialize(new { profile.Name, profile.Instructions, profile.WorkflowAccess,
                        TableAccess = profile.EffectiveTableAccess, SourceAccess = profile.EffectiveSourceAccess, ArtifactAccess = profile.EffectiveArtifactAccess,
                        LinkedWorkflowIds = profile.WorkflowAccess == "none" ? [] : profile.WorkflowIds }))
        }, new JsonObject { ["role"] = "user", ["content"] = payload ?? "{}" });
        await model.ResetSessionAsync();
        try
        {
            for (var turn = 0; turn < 20; turn++)
            {
                token.ThrowIfCancellationRequested();
                currentTurn = turn + 1;
                if (messages.ToJsonString().Length > 12_000_000)
                {
                    throw new InvalidOperationException("Agent context exceeded its limit; split the task into smaller steps.");
                }

                report?.Invoke(new(DateTimeOffset.UtcNow, "model", $"Waiting for {modelName}", Turn: turn + 1));
                var modelStarted = Stopwatch.GetTimestamp();
                var reported = false;
                var reply = await model.CompleteAsync(connectionId!.Value, modelName!, messages, schema, token, text =>
                {
                    reported = true;
                    report?.Invoke(new(DateTimeOffset.UtcNow, "update", text.Length > 2000 ? text[..2000] + "…" : text, Turn: turn + 1));
                });
                report?.Invoke(new(DateTimeOffset.UtcNow, "response", "Model response received", Turn: turn + 1,
                    DurationSeconds: Stopwatch.GetElapsedTime(modelStarted).TotalSeconds));
                reply["role"] = "assistant";
                messages.Add(reply.DeepClone());
                if (reply["tool_calls"] is not JsonArray { Count: > 0 } calls)
                {
                    var content = reply["content"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidOperationException("The agent returned no result.");
                    }

                    try
                    {
                        var output = JsonNode.Parse(content) as JsonObject ?? throw new JsonException();
                        report?.Invoke(new(DateTimeOffset.UtcNow, "completed", $"Result ready for {action.ResponseField}", Turn: turn + 1));
                        return output;
                    }
                    catch (JsonException)
                    {
                        throw new InvalidOperationException("The agent must return a complete JSON object. No result was accepted.");
                    }
                }
                if (!reported && reply["content"] is JsonValue update && update.TryGetValue<string>(out var updateText)
                    && !string.IsNullOrWhiteSpace(updateText))
                {
                    report?.Invoke(new(DateTimeOffset.UtcNow, "update", updateText.Length > 2000 ? updateText[..2000] + "…" : updateText, Turn: turn + 1));
                }

                if (calls.Count > 8 || calls.Any(call => string.IsNullOrWhiteSpace(call?["id"]?.GetValue<string>()))
                    || calls.Select(call => call!["id"]!.GetValue<string>()).Distinct().Count() != calls.Count)
                {
                    throw new InvalidOperationException("The agent returned invalid tool calls.");
                }

                foreach (var call in calls)
                {
                    token.ThrowIfCancellationRequested();
                    var name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                    string result;
                    var toolStarted = Stopwatch.GetTimestamp();
                    Guid? jobId = null;
                    Guid? jobRunId = null;
                    var toolName = AllowedTools.Contains(name) ? name : "unavailable tool";
                    try
                    {
                        if (!functions.TryGetValue(name, out var function))
                        {
                            throw new ArgumentException("Tool not available to this workflow agent.");
                        }

                        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(call!["function"]!["arguments"]!.GetValue<string>())
                            ?? throw new ArgumentException("Tool arguments are required.");
                        var arguments = JsonNode.Parse(call!["function"]!["arguments"]!.GetValue<string>());
                        jobId = ReadId(arguments, "jobId");
                        report?.Invoke(new(DateTimeOffset.UtcNow, "tool_started", ToolLabel(name), turn + 1, name, jobId));
                        result = JsonSerializer.Serialize(await function.InvokeAsync(new AIFunctionArguments(args), token));
                        var output = JsonNode.Parse(result);
                        if (name is "run_job" or "get_job_run")
                        {
                            jobRunId = ReadId(output, "id");
                            jobId = ReadId(output, "jobId") ?? jobId;
                        }
                        else if (name is "save_job" or "get_job")
                        {
                            jobId = ReadId(output, "id") ?? jobId;
                        }

                        var status = ReadValue(output, "status");
                        var failed = status is "Failed" or "Cancelled" or "Partial" || ReadValue(output, "error") is not null;
                        report?.Invoke(new(DateTimeOffset.UtcNow, failed ? "tool_failed" : "tool_completed",
                            failed ? "Tool returned a failure or incomplete result" : "Tool completed",
                            turn + 1, name, jobId, jobRunId, Stopwatch.GetElapsedTime(toolStarted).TotalSeconds));
                        if (result.Length > 500_000)
                        {
                            report?.Invoke(new(DateTimeOffset.UtcNow, "warning", "Tool output exceeded the agent context limit; the agent was asked to inspect a smaller report.", turn + 1, name, jobId, jobRunId));
                            result = JsonSerializer.Serialize(new { error = "Tool result exceeds the context limit. Inspect a smaller job or report; this result is not evidence of success." });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        report?.Invoke(new(DateTimeOffset.UtcNow, "tool_failed", "Tool failed; error returned to the agent", turn + 1,
                            toolName, jobId, DurationSeconds: Stopwatch.GetElapsedTime(toolStarted).TotalSeconds));
                        result = JsonSerializer.Serialize(new { error = ex.Message });
                    }
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call!["id"]!.DeepClone(), ["content"] = result });
                }
            }
            throw new InvalidOperationException("Agent reached its 20-turn limit without a final result. Completed job actions remain applied.");
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            report?.Invoke(new(DateTimeOffset.UtcNow, "failed", "Agent exceeded its 30-minute execution limit"));
            throw new TimeoutException("Agent exceeded its 30-minute execution limit. Completed job actions remain applied.", ex);
        }
        catch (OperationCanceledException)
        {
            report?.Invoke(new(DateTimeOffset.UtcNow, "cancelled", "Agent execution cancelled"));
            throw;
        }
        catch (Exception)
        {
            report?.Invoke(new(DateTimeOffset.UtcNow, "failed", "Agent execution failed; see the step error"));
            throw;
        }
        finally { await model.ResetSessionAsync(); }
    }

    private static string? ReadValue(JsonNode? node, string key) => node is JsonObject obj
        ? obj.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.ToString() : null;
    private static Guid? ReadId(JsonNode? node, string key) => Guid.TryParse(ReadValue(node, key), out var id) ? id : null;
    private static string ToolLabel(string name) => name switch
    {
        "job_authoring_guide" => "Reading job authoring guide",
        "list_jobs" => "Listing project jobs",
        "get_job" => "Inspecting job source and configuration",
        "save_job" => "Saving job source and configuration",
        "run_job" => "Running job and waiting for its result",
        "get_job_run" => "Inspecting job execution results",
        "list_job_runs" => "Checking job execution history",
        _ => "Calling tool"
    };
}
