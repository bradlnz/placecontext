using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Infrastructure.AgentWorkflows;

internal sealed record AgentDefinition(AgentWorkflow Workflow, string[] RequiredFields, string Instruction);
