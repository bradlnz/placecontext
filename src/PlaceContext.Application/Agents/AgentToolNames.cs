namespace PlaceContext.Application.Agents;

/// <summary>
/// Canonical names for agent/chat tools. Use these everywhere a tool is dispatched, catalogued,
/// or matched in model output — never hardcode the string.
/// </summary>
public static class AgentToolNames
{
    public const string ListTables = "list_tables";
    public const string QueryTable = "query_table";
    public const string ListJobs = "list_jobs";
    public const string ListJobRuns = "list_job_runs";
    public const string ListChains = "list_chains";
    public const string RunJob = "run_job";
    public const string RunJobChain = "run_job_chain";
    public const string Search = "search";
    public const string QueryGraph = "query_graph";
    public const string RenderGraph = "render_graph";
    public const string GetArtifacts = "get_artifacts";
    public const string ShowArtifact = "show_artifact";
    public const string ListSchedules = "list_schedules";
    public const string ScheduleJob = "schedule_job";
    public const string ToggleSchedule = "toggle_schedule";
    public const string CallMcp = "call_mcp";
    public const string ListMcpTools = "list_mcp_tools";
    public const string RenderMap = "render_map";

    public const string ToolCallPrefix = "[[tool:";
    public const string ToolCallSuffix = "]]";

    public static string FormatCall(string name, string args = "")
        => $"{ToolCallPrefix}{name}|{args}{ToolCallSuffix}";
}
