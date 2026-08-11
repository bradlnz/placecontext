using PlaceContext.Application.Agents;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public sealed class AgentWorkspaceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ensure_command_agent_is_idempotent()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new EnsureCommandAgentHandler(repository, unitOfWork, new FakeClock(Now));
        var projectId = Guid.NewGuid();

        var first = await handler.HandleAsync(new EnsureCommandAgentCommand(projectId));
        var second = await handler.HandleAsync(new EnsureCommandAgentCommand(projectId));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(AgentKind.Command, first.Kind);
        Assert.Single(await repository.ListForProjectAsync(projectId));
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Save_worker_from_template_applies_template_defaults_and_graph_access()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new SaveAgentDefinitionHandler(repository, new InMemoryJobRepository(), unitOfWork, new FakeClock(Now));
        var projectId = Guid.NewGuid();

        var view = await handler.HandleAsync(new SaveAgentDefinitionCommand(
            projectId, null, "Researcher", "Find evidence", "", "research",
            [], [], null, true));

        Assert.Equal("research", view.TemplateKey);
        Assert.Contains(AgentCapability.GraphRead, view.Capabilities);
        Assert.Contains(AgentCapability.DataRead, view.Capabilities);
        Assert.Contains(AgentCapability.ArtifactsRead, view.Capabilities);
        Assert.NotEmpty(view.Instructions);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Save_worker_defaults_parent_to_command_when_not_provided()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new SaveAgentDefinitionHandler(repository, new InMemoryJobRepository(), unitOfWork, new FakeClock(Now));
        var projectId = Guid.NewGuid();

        var command = AgentDefinition.CreateCommand(projectId, Now);
        await repository.AddAsync(command);

        var view = await handler.HandleAsync(new SaveAgentDefinitionCommand(
            projectId, null, "Researcher", "Find evidence", "", "research",
            [], [], null, true));

        Assert.Equal(command.Id, view.ParentAgentId);
    }

    [Fact]
    public async Task Save_worker_rejects_parent_cycles()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var handler = new SaveAgentDefinitionHandler(repository, new InMemoryJobRepository(), new RecordingUnitOfWork(), new FakeClock(Now));
        var projectId = Guid.NewGuid();

        var command = AgentDefinition.CreateCommand(projectId, Now);
        var parent = AgentDefinition.CreateWorker(
            projectId, "Parent", "", "", "research", [AgentCapability.GraphRead], [], command.Id, Now);
        var child = AgentDefinition.CreateWorker(
            projectId, "Child", "", "", "research", [AgentCapability.GraphRead], [], parent.Id, Now);

        await repository.AddAsync(command);
        await repository.AddAsync(parent);
        await repository.AddAsync(child);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveAgentDefinitionCommand(
                projectId, parent.Id, "Parent", "", "", "research", [AgentCapability.GraphRead],
                [], child.Id, true)));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Command_agent_cannot_have_parent()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var handler = new SaveAgentDefinitionHandler(repository, new InMemoryJobRepository(), new RecordingUnitOfWork(), new FakeClock(Now));
        var projectId = Guid.NewGuid();

        var command = AgentDefinition.CreateCommand(projectId, Now);
        await repository.AddAsync(command);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new SaveAgentDefinitionCommand(
                projectId, command.Id, command.Name, command.Description, command.Instructions,
                command.TemplateKey, command.Capabilities, command.AllowedJobIds, Guid.NewGuid(), command.Enabled)));

        Assert.Contains("command agent cannot have a parent", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Command_agent_cannot_be_deleted()
    {
        var repository = new InMemoryAgentDefinitionRepository();
        var command = AgentDefinition.CreateCommand(Guid.NewGuid(), Now);
        await repository.AddAsync(command);
        var handler = new DeleteAgentDefinitionHandler(repository, new RecordingUnitOfWork());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new DeleteAgentDefinitionCommand(command.Id)));

        Assert.Contains("Command Agent", error.Message);
    }

    [Fact]
    public void Tool_authorization_enforces_capability_and_job_allowlist()
    {
        var allowedJob = Guid.NewGuid();
        var deniedJob = Guid.NewGuid();
        var agent = AgentDefinition.CreateWorker(
            Guid.NewGuid(), "Operator", "", "", "job-operator",
            [AgentCapability.JobsRead, AgentCapability.JobsRun], [allowedJob], null, Now);

        Assert.True(AgentToolAuthorization.CanUse(agent, AgentToolNames.ListJobs, ""));
        Assert.True(AgentToolAuthorization.CanUse(agent, AgentToolNames.RunJob, allowedJob.ToString()));
        Assert.False(AgentToolAuthorization.CanUse(agent, AgentToolNames.RunJob, deniedJob.ToString()));
        Assert.False(AgentToolAuthorization.CanUse(agent, AgentToolNames.RunJobChain, Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task Command_agent_runs_multiple_workers_and_synthesizes_graph_grounded_contributions()
    {
        var projectId = Guid.NewGuid();
        var repository = new InMemoryAgentDefinitionRepository();
        var research = AgentDefinition.CreateWorker(projectId, "Research", "", "Find evidence", "research",
            [AgentCapability.GraphRead, AgentCapability.DataRead], [], null, Now);
        var operations = AgentDefinition.CreateWorker(projectId, "Operations", "", "Plan execution", "operations",
            [AgentCapability.GraphRead, AgentCapability.JobsRun], [], null, Now);
        await repository.AddAsync(AgentDefinition.CreateCommand(projectId, Now));
        await repository.AddAsync(research);
        await repository.AddAsync(operations);
        var gateway = new ScriptedProjectChatGateway(
            $"{research.Id},{operations.Id}", "evidence contribution", "execution contribution");
        var orchestrator = new CommandAgentOrchestrator(repository, gateway, new FakeClock(Now));

        var route = await orchestrator.RouteAsync(projectId, "Achieve the goal", "graph fact: revenue rose");

        Assert.Equal(2, route.CollaboratingAgents.Count);
        Assert.Contains("Research", route.PromptSection);
        Assert.Contains("Operations", route.PromptSection);
        Assert.Contains("evidence contribution", route.PromptSection);
        Assert.Contains("execution contribution", route.PromptSection);
        Assert.All(gateway.Calls.Skip(1), messages =>
            Assert.Contains("graph fact: revenue rose", messages[0].Content));
    }

    private sealed class ScriptedProjectChatGateway(params string[] responses) : IProjectChatGateway
    {
        private readonly Queue<string> _responses = new(responses);
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<ProjectChatStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(new ProjectChatStatus(ProjectChatBackend.LocalCluster, true, "Local agent cluster"));

        public Task<string> ChatAsync(Guid projectId, IReadOnlyList<ChatMessage> messages,
            ChatSettings? settings = null, CancellationToken ct = default)
        {
            Calls.Add(messages);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
