
namespace PlaceContext.Application.AgentWorkflows;

internal sealed record AgentDefinition(AgentWorkflow Workflow, string[] RequiredFields, string Instruction);
