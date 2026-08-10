using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Agents;

public static class AgentTemplateCatalog
{
    public static readonly IReadOnlyList<AgentTemplateView> All =
    [
        new("research", "Research Agent", "Finds and synthesizes evidence from project knowledge.",
            "Investigate the request using the project data graph, project data, and artifacts. Cite the concrete records or artifacts used.",
            [AgentCapability.GraphRead, AgentCapability.DataRead, AgentCapability.ArtifactsRead]),
        new("analyst", "Data Analyst", "Analyses project data and explains patterns.",
            "Use the project data graph to identify relevant tables, then analyse the data and explain findings with evidence.",
            [AgentCapability.GraphRead, AgentCapability.DataRead, AgentCapability.ArtifactsRead]),
        new("job-operator", "Job Operator", "Runs explicitly approved project jobs.",
            "Inspect job definitions and run only jobs explicitly allowed for this agent. Report the resulting run identifiers and status.",
            [AgentCapability.GraphRead, AgentCapability.JobsRead, AgentCapability.JobsRun, AgentCapability.ChainsRead, AgentCapability.ChainsRun]),
        new("report-writer", "Report Writer", "Creates grounded summaries from project outputs.",
            "Use the project data graph and approved artifacts as evidence. Never invent facts not present in project knowledge.",
            [AgentCapability.GraphRead, AgentCapability.DataRead, AgentCapability.ArtifactsRead]),
    ];

    public static AgentTemplateView? Find(string? key)
        => All.FirstOrDefault(template => string.Equals(template.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
