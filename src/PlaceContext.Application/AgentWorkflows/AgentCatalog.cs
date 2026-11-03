using PlaceContext.Application.Shared;

namespace PlaceContext.Application.AgentWorkflows;

internal static class AgentCatalog
{
    internal static readonly AgentDefinition[] Definitions =
    [
        Define("retail", "Retail", "Stock planning", ["products", "sales", "stock"],
            PromptText.Read("AgentWorkflows.Prompts.StockPlanning")),
        Define("services", "Professional services", "Client onboarding", ["client", "scope", "documents"],
            PromptText.Read("AgentWorkflows.Prompts.ClientOnboarding")),
        Define("property", "Property operations", "Maintenance triage", ["property", "issue", "access"],
            PromptText.Read("AgentWorkflows.Prompts.MaintenanceTriage"))
    ];

    internal static IReadOnlyList<AgentWorkflow> Workflows => Definitions.SelectMany(d => new[]
    {
        d.Workflow with { Id = d.Workflow.Id + "-checklist", Name = d.Workflow.Name + " checklist", Paid = false,
            Description = "Check that the required information is present. No AI tokens used.", Steps = ["Collect information", "Check required fields", "Return missing information"] },
        d.Workflow
    }).ToArray();

    private static AgentDefinition Define(string id, string industry, string name, string[] fields, string instruction) => new(
        new(id, name, industry, "Create a detailed plan from your information, with Jev verification and a human review path.", true,
            ["Validate inputs", "Prepare a detailed plan", "Verify with Jev", "Deliver the plan or request review"],
            fields),
        fields, instruction);
}
