using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Infrastructure.AgentWorkflows;

internal static class AgentCatalog
{
    internal static readonly AgentDefinition[] Definitions =
    [
        Define("retail", "Retail", "Stock planning", ["products", "sales", "stock"],
            "Compare supplied stock and sales, identify shortages and surplus, and produce a prioritized replenishment proposal. Do not place orders."),
        Define("services", "Professional services", "Client onboarding", ["client", "scope", "documents"],
            "Review the supplied client scope and documents, identify missing onboarding information, and produce a sequenced onboarding plan with responsibilities. Do not contact anyone."),
        Define("property", "Property operations", "Maintenance triage", ["property", "issue", "access"],
            "Review the supplied maintenance request, identify missing information, and produce a triage and handoff plan. Do not book contractors or claim an inspection occurred.")
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
