namespace PlaceContext.Application.Build;

public sealed class DashboardAgentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New agent";
    public Guid ConnectionId { get; set; }
    public string Model { get; set; } = "";
    public string Instructions { get; set; } = "";
    public List<Guid> McpConnectionIds { get; set; } = [];
    public List<Guid> SkillIds { get; set; } = [];
    public string WorkflowAccess { get; set; } = "manage";
    public string DataAccess { get; set; } = "manage";

    public bool AllowsTool(string name) => name switch
    {
        "ask_user" or "project_context" or "show_chart" or "remember" or "job_authoring_guide" => true,
        "list_mcp_tools" or "call_mcp_tool" => McpConnectionIds.Count > 0,
        "project_activity" or "get_job_run" or "list_jobs" or "get_job" or "list_job_runs"
            or "get_workflow" or "check_workflow_readiness" or "list_workflows" or "list_workflow_runs"
            or "review_workflow_result" or "list_schedules" => WorkflowAccess != "none",
        "run_job" or "run_workflow" => WorkflowAccess is "run" or "manage",
        "save_job" or "delete_job" or "save_workflow" or "delete_workflow"
            or "create_schedule" or "update_schedule" or "delete_schedule" => WorkflowAccess == "manage",
        "source_connectors" or "list_data_sources" or "search_data_sources" or "list_project_tables"
            or "list_artifacts" => DataAccess != "none",
        "prepare_data_source" => DataAccess == "manage",
        "create_data_pipeline" => DataAccess == "manage" && WorkflowAccess == "manage",
        "show_live_chart" => DataAccess != "none" || WorkflowAccess != "none",
        _ => false
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60 || Model is null || Model.Length > 200
            || Instructions is null || Instructions.Length > 8000
            || McpConnectionIds is null || McpConnectionIds.Count > 32 || McpConnectionIds.Contains(Guid.Empty)
            || McpConnectionIds.Distinct().Count() != McpConnectionIds.Count
            || SkillIds is null || SkillIds.Count > 32 || SkillIds.Contains(Guid.Empty)
            || SkillIds.Distinct().Count() != SkillIds.Count
            || WorkflowAccess is not ("none" or "read" or "run" or "manage")
            || DataAccess is not ("none" or "read" or "manage"))
            throw new ArgumentException("Use a name up to 60 characters, instructions up to 8,000, valid access levels, and up to 32 distinct skills and MCP connections.");
    }
}
