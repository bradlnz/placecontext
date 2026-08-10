using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Agents;

public static class AgentToolAuthorization
{
    public static bool CanUse(AgentDefinition agent, string toolName, string args)
    {
        if (!agent.Enabled)
            return false;

        var required = RequiredCapability(toolName);
        if (required is null || !agent.Capabilities.Contains(required.Value))
            return false;

        if (toolName != AgentToolNames.RunJob || agent.AllowedJobIds.Count == 0)
            return true;

        var value = args.Split('|', 2)[0].Trim();
        return Guid.TryParse(value, out var jobId) && agent.AllowedJobIds.Contains(jobId);
    }

    public static AgentCapability? RequiredCapability(string toolName) => toolName switch
    {
        AgentToolNames.QueryGraph or AgentToolNames.RenderGraph => AgentCapability.GraphRead,
        AgentToolNames.ListTables or AgentToolNames.QueryTable or AgentToolNames.Search or AgentToolNames.RenderMap => AgentCapability.DataRead,
        AgentToolNames.GetArtifacts or AgentToolNames.ShowArtifact => AgentCapability.ArtifactsRead,
        AgentToolNames.ListJobs or AgentToolNames.ListJobRuns => AgentCapability.JobsRead,
        AgentToolNames.RunJob => AgentCapability.JobsRun,
        AgentToolNames.ListChains => AgentCapability.ChainsRead,
        AgentToolNames.RunJobChain => AgentCapability.ChainsRun,
        AgentToolNames.ListSchedules => AgentCapability.SchedulesRead,
        AgentToolNames.ScheduleJob or AgentToolNames.ToggleSchedule => AgentCapability.SchedulesManage,
        AgentToolNames.CallMcp or AgentToolNames.ListMcpTools => AgentCapability.McpCall,
        _ => null,
    };
}
