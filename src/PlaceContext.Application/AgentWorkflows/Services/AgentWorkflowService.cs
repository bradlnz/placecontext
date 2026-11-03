using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Build;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
namespace PlaceContext.Application.AgentWorkflows;

public sealed class AgentWorkflowService(IServiceScopeFactory scopes, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions, AgentWorkflowPolicy policy,
    IModelConnections connections, IAgentModelGateway model, IOutcomeReviewer reviewer, IDataEncryptor encryption,
    IClock clock) : IAgentWorkflows
{
    private const string OutputPurpose = "agent.workflow.output.v1";
    private bool SimulationEnabled => policy.SimulationEnabled;

    private async Task CheckAsync(IAgentWorkflowStore store, Guid projectId, string permission, CancellationToken ct)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty || !user.IsAuthenticated
            || !await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(permission, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this workflow operation.");
        }
        if (!await store.ProjectExistsAsync(projectId, ct))
        {
            throw new InvalidOperationException("Project not found.");
        }
    }

    public async Task<AgentWorkspace> LoadAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentWorkflowStore>();
        await CheckAsync(store, projectId, Permission.ChainsManage, ct);
        var subscriptions = await store.ListSubscriptionsAsync(ct);
        var runs = await store.ListRunsAsync(projectId, ct);
        return new(SimulationEnabled, await permissions.HasAsync(Permission.SettingsManage, ct), AgentCatalog.Workflows,
            subscriptions.Select(x => new AgentSubscription(x.AgentId, x.Active && SimulationEnabled, x.UpdatedAt)).ToArray(),
            runs.Select(View).ToArray());
    }

    public async Task SetSubscriptionAsync(Guid projectId, string agentId, bool active, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentWorkflowStore>();
        await CheckAsync(store, projectId, Permission.SettingsManage, ct);
        if (!SimulationEnabled)
        {
            throw new InvalidOperationException("Subscription simulation is available only in Development when explicitly enabled.");
        }
        if (!AgentCatalog.Definitions.Any(x => x.Workflow.Id == agentId))
        {
            throw new ArgumentException("Paid agent not found.");
        }
        await store.SetSubscriptionAsync(agentId, active, clock.UtcNow, ct);
    }

    public async Task<AgentRun> RunAsync(Guid projectId, string agentId, Guid requestId, string input, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentWorkflowStore>();
        await CheckAsync(store, projectId, Permission.ChainsManage, ct);
        if (!await permissions.HasAsync(Permission.JobsRun, ct))
        {
            throw new UnauthorizedAccessException("Workflow execution permission is required.");
        }
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A run request ID is required.");
        }
        var workflow = AgentCatalog.Workflows.SingleOrDefault(x => x.Id == agentId)
                    ?? throw new ArgumentException("Workflow not found.");
        if (string.IsNullOrWhiteSpace(input) || input.Length > 16000)
        {
            throw new ArgumentException("Provide a JSON object of up to 16,000 characters.");
        }
        JsonObject data;
        try
        {
            data = JsonNode.Parse(input) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new ArgumentException("Provide the workflow inputs as a JSON object.");
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToJsonString())));
        var existing = await store.GetRunAsync(requestId, ct);
        if (existing is not null)
        {
            return Replay(existing, projectId, agentId, hash);
        }
        if (workflow.Paid && (!SimulationEnabled
                    || !await store.IsSubscribedAsync(agentId, ct)))
        {
            throw new UnauthorizedAccessException("Activate a simulated subscription for this agent before starting a new run.");
        }
        var definition = AgentCatalog.Definitions.Single(x => agentId == x.Workflow.Id || agentId == x.Workflow.Id + "-checklist");
        var row = new AgentWorkflowRun
        {
            Id = requestId,
            ProjectId = projectId,
            AgentId = agentId,
            InputHash = hash,
            Status = AgentRunStatus.Running,
            StartedAt = clock.UtcNow
        };
        if (!await store.TryCreateRunAsync(row, ct))
        {
            var duplicate = await store.GetRunAsync(requestId, ct)
                ?? throw new InvalidOperationException("The concurrent workflow run could not be loaded.");
            return Replay(duplicate, projectId, agentId, hash);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var missing = definition.RequiredFields.Where(field => Missing(data[field])).ToArray();
            async Task CompleteWorkflowAsync()
            {
                if (missing.Length > 0 || !workflow.Paid)
                {
                    row.InputTokens = row.OutputTokens = 0;
                    row.DemoCredits = 0;
                    row.Status = missing.Length > 0 ? AgentRunStatus.NeedsReview : AgentRunStatus.Completed;
                    row.Output = encryption.Protect(JsonSerializer.Serialize(new
                    {
                        missing,
                        nextStep = missing.Length > 0 ? "Supply the missing information." : "Required fields are present. Contents have not been verified."
                    }), OutputPurpose);
                    return;
                }
                {
                    var connection = (await connections.ListAsync(deadline.Token)).FirstOrDefault(x => x.Enabled && x.Models.Count > 0)
                        ?? throw new InvalidOperationException("No enabled model connection.");
                    var completion = await model.CompleteMeteredAsync(connection.Id, connection.Models[0],
                        definition.Instruction + PromptText.Read("AgentWorkflows.Prompts.PlanConstraints"), data.ToJsonString(), deadline.Token);
                    AgentWorkflowUsage? usage = null;
                    if (completion.InputTokens is >= 0 && completion.OutputTokens is >= 0)
                    {
                        row.InputTokens = completion.InputTokens;
                        row.OutputTokens = completion.OutputTokens;
                        // shortcut: Jev usage is unavailable; reconcile all providers before enabling real billing.
                        row.DemoCredits = ((decimal)completion.InputTokens.Value + completion.OutputTokens.Value) / 1000m;
                        usage = new(projectId, completion.Model, completion.InputTokens.Value, completion.OutputTokens.Value,
                            $"Agent workflow: {workflow.Name} (simulated subscription)", clock.UtcNow);
                    }
                    // Persist reported model usage before verification; Jev failure must not erase consumed tokens.
                    await store.SaveRunAsync(row, usage, CancellationToken.None);
                    if (string.IsNullOrWhiteSpace(completion.Text) || completion.Text.Length > 16000)
                    {
                        throw new InvalidOperationException("Invalid workflow output.");
                    }
                    row.Output = encryption.Protect(completion.Text, OutputPurpose);
                    row.Status = AgentRunStatus.NeedsReview;
                    row.Notice = "The plan needs human review. Jev verification was unavailable or uncertain.";
                    try
                    {
                        var review = await reviewer.EvaluateAsync(
                            PromptText.Read("AgentWorkflows.Prompts.PlanReview"),
                            JsonSerializer.Serialize(new
                            {
                                task = definition.Instruction,
                                input = data,
                                plan = completion.Text
                            }), deadline.Token, projectId);
                        if (review["noul"] is JsonValue value && value.TryGetValue<double>(out var score)
                            && double.IsFinite(score) && score >= 0.9 && score <= 1)
                        {
                            row.Status = AgentRunStatus.Completed;
                            row.Notice = "Jev verified the plan. Proposed actions still require your approval and execution.";
                        }
                    }
                    catch (Exception) when (!deadline.IsCancellationRequested) { /* Unavailable verification stays on the human-review path. */ }
                    if (row.InputTokens is null)
                    {
                        row.Notice += " Token usage was not reported; no demo charge was calculated.";
                    }
                }
            }
            await CompleteWorkflowAsync();
        }
        catch (OperationCanceledException)
        {
            row.Status = AgentRunStatus.Interrupted;
            row.Notice = "Execution was interrupted. Reported usage is retained; unreported provider usage may still have occurred. Start a new run explicitly to retry.";
        }
        catch (Exception)
        {
            row.Status = AgentRunStatus.Failed;
            row.Notice = "The workflow could not finish. Check the model connection and inputs. Any recorded usage is retained.";
        }
        row.FinishedAt = clock.UtcNow;
        await store.SaveRunAsync(row, null, CancellationToken.None);
        return View(row);
    }

    private static bool Missing(JsonNode? value) => value is null
        || value is JsonValue text && text.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s)
        || value is JsonArray { Count: 0 } || value is JsonObject { Count: 0 };

    private AgentRun Replay(AgentWorkflowRun row, Guid projectId, string agentId, string hash)
    {
        if (row.ProjectId != projectId || row.AgentId != agentId || row.InputHash != hash)
        {
            throw new InvalidOperationException("This request ID already belongs to a different workflow request.");
        }
        return View(row);
    }

    private AgentRun View(AgentWorkflowRun row)
    {
        var interrupted = row.Status == AgentRunStatus.Running && row.StartedAt < clock.UtcNow.AddMinutes(-15);
        return new(row.Id, row.AgentId, interrupted ? AgentRunStatus.Interrupted : row.Status,
            row.Output is null ? null : encryption.Unprotect(row.Output, OutputPurpose),
            interrupted ? "Execution stopped without a final result. Check recorded usage before starting a new run." : row.Notice,
            row.InputTokens, row.OutputTokens, row.DemoCredits, row.StartedAt, row.FinishedAt);
    }
}
