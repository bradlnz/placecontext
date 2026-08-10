using System.Text;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Agents;

/// <summary>Central command agent that chooses a least-privileged worker before each turn.</summary>
public sealed class CommandAgentOrchestrator(
    IAgentDefinitionRepository repository,
    IProjectChatGateway chat,
    IClock clock)
{
    public async Task<CommandAgentRoute> RouteAsync(
        Guid projectId,
        string request,
        string graphContext = "",
        CancellationToken ct = default)
    {
        var agents = await repository.ListForProjectAsync(projectId, ct);
        var command = agents.FirstOrDefault(agent => agent.Kind == AgentKind.Command)
            ?? AgentDefinition.CreateCommand(projectId, clock.UtcNow);
        var workers = agents.Where(agent => agent.Kind == AgentKind.Worker && agent.Enabled).ToArray();
        IReadOnlyList<AgentDefinition> collaborators = [];
        IReadOnlyList<string> contributions = [];

        var status = await chat.GetStatusAsync(projectId, ct);
        if (workers.Length > 0 && status.IsEnabled)
        {
            var routerPrompt = BuildRouterPrompt(command, workers, request);
            var choice = await chat.ChatAsync(projectId,
                [new ChatMessage("system", routerPrompt), new ChatMessage("user", request)],
                new ChatSettings(Temperature: 0f, MaxTokens: 80), ct);
            collaborators = workers
                .Where(worker => choice.Contains(worker.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToArray();
            if (collaborators.Count > 0)
            {
                contributions = await Task.WhenAll(collaborators.Select(worker =>
                    RunWorkerSafelyAsync(projectId, worker, request, graphContext, ct)));
            }
        }

        return new CommandAgentRoute(command, collaborators,
            BuildExecutionPrompt(command, collaborators, contributions));
    }

    private static string BuildRouterPrompt(
        AgentDefinition command,
        IReadOnlyList<AgentDefinition> workers,
        string request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(command.Instructions);
        prompt.AppendLine("Choose the least-privileged enabled workers that should collaborate on the request.");
        prompt.AppendLine("Return only their UUIDs, comma separated. Select up to four. Return COMMAND when no worker is suitable.");
        prompt.AppendLine("Workers:");
        foreach (var worker in workers)
            prompt.AppendLine($"- {worker.Id}: {worker.Name} — {worker.Description} — capabilities: {string.Join(", ", worker.Capabilities)}");
        prompt.AppendLine($"Request: {request}");
        return prompt.ToString();
    }

    private async Task<string> RunWorkerAsync(
        Guid projectId,
        AgentDefinition worker,
        string request,
        string graphContext,
        CancellationToken ct)
    {
        var prompt = $"""
            You are {worker.Name}, collaborating under the Command Agent.
            {worker.Instructions}
            Your capabilities are: {string.Join(", ", worker.Capabilities)}.
            Use the project data graph context as the authoritative source. Produce a concise contribution for the Command Agent. Do not call tools; recommend any necessary actions instead.

            ## Project data graph context
            {graphContext}
            """;
        return await chat.ChatAsync(projectId,
            [new ChatMessage("system", prompt), new ChatMessage("user", request)],
            new ChatSettings(Temperature: 0.2f), ct);
    }

    private async Task<string> RunWorkerSafelyAsync(
        Guid projectId,
        AgentDefinition worker,
        string request,
        string graphContext,
        CancellationToken ct)
    {
        try
        {
            return await RunWorkerAsync(projectId, worker, request, graphContext, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return "This collaborator was unavailable. Continue with the remaining contributions.";
        }
    }

    private static string BuildExecutionPrompt(
        AgentDefinition command,
        IReadOnlyList<AgentDefinition> collaborators,
        IReadOnlyList<string> contributions)
    {
        var collaboration = collaborators.Count == 0
            ? "The Command Agent is handling this request directly."
            : string.Join("\n\n", collaborators.Select((agent, index) =>
                $"### {agent.Name}\n{contributions[index]}"));
        var authority = collaborators.Count == 0
            ? string.Join(", ", command.Capabilities)
            : string.Join("; ", collaborators.Select(agent =>
                $"{agent.Name}: {string.Join(", ", agent.Capabilities)}"));
        return $"""
            ## Command Agent orchestration
            Command agent: {command.Name}
            Collaborating agents: {(collaborators.Count == 0 ? "none" : string.Join(", ", collaborators.Select(agent => agent.Name)))}

            {command.Instructions}

            The project data graph is the authoritative source of project knowledge. Ground every answer and action in the graph context supplied below. Synthesize the collaborators' contributions into one coherent response. A tool may be used only when at least one collaborating agent has authority for that exact action. Active authority: {authority}.

            ## Collaborator contributions
            {collaboration}
            """;
    }
}
