namespace PlaceContext.Application.Build;

public sealed class DashboardAgentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New agent";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Id == DashboardSettings.DefaultAgentId ? "Master agent" : Name;
    public Guid? ParentAgentId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? EffectiveParentAgentId => Id == DashboardSettings.DefaultAgentId ? null : ParentAgentId ?? DashboardSettings.DefaultAgentId;
    public Guid ConnectionId { get; set; }
    public string Model { get; set; } = "";
    public string Instructions { get; set; } = "";
    public List<Guid> McpConnectionIds { get; set; } = [];
    public List<Guid> WorkflowIds { get; set; } = [];
    public string WorkflowAccess { get; set; } = "manage";
    public string DataAccess { get; set; } = "manage"; // Preserve access for profiles saved before separate data permissions.
    public string? TableAccess { get; set; }
    public string? SourceAccess { get; set; }
    // Null preserves legacy access; an empty list explicitly disables every source capability.
    public List<string>? SourceCapabilities { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EffectiveSourceCapabilities => SourceCapabilities ?? (EffectiveSourceAccess switch
    {
        "manage" => ["metadata", "search", "connect", "pipeline"],
        "read" => ["metadata", "search"],
        _ => []
    });
    public string? ArtifactAccess { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveTableAccess => TableAccess ?? (DataAccess == "none" ? "none" : "read");
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveSourceAccess => SourceAccess ?? DataAccess;
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveArtifactAccess => ArtifactAccess ?? (DataAccess == "none" ? "none" : "read");

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
        "source_connectors" => EffectiveSourceCapabilities.Contains("metadata") || EffectiveSourceCapabilities.Contains("connect"),
        "list_data_sources" => EffectiveSourceCapabilities.Contains("metadata"),
        "search_data_sources" => EffectiveSourceCapabilities.Contains("search"),
        "list_project_tables" => EffectiveTableAccess == "read",
        "list_artifacts" => EffectiveArtifactAccess == "read",
        "prepare_data_source" => EffectiveSourceCapabilities.Contains("connect"),
        "create_data_pipeline" => EffectiveSourceCapabilities.Contains("pipeline") && WorkflowAccess == "manage",
        "show_live_chart" => EffectiveTableAccess == "read" || WorkflowAccess != "none",
        _ => false
    };

    public static void ValidateHierarchy(IReadOnlyList<DashboardAgentProfile> agents)
    {
        var byId = agents.ToDictionary(a => a.Id);
        foreach (var agent in agents)
        {
            var visited = new HashSet<Guid> { agent.Id };
            var parentId = agent.EffectiveParentAgentId;
            while (parentId is { } id && id != DashboardSettings.DefaultAgentId)
            {
                if (!visited.Add(id)) throw new ArgumentException("An agent cannot report to itself or one of its descendants.");
                if (!byId.TryGetValue(id, out var parent)) throw new ArgumentException("Choose an available parent agent. Reassign its children before deleting it.");
                parentId = parent.EffectiveParentAgentId;
            }
        }
    }

    public void Validate()
    {
        if (ParentAgentId == Guid.Empty || ParentAgentId == Id || Id == DashboardSettings.DefaultAgentId && ParentAgentId is not null)
            throw new ArgumentException("Choose another agent as parent. The master agent has no parent.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60 || Model is null || Model.Length > 200
            || Instructions is null || Instructions.Length > 8000
            || McpConnectionIds is null || McpConnectionIds.Count > 32 || McpConnectionIds.Contains(Guid.Empty)
            || McpConnectionIds.Distinct().Count() != McpConnectionIds.Count
            || WorkflowIds is null || WorkflowIds.Count > 200 || WorkflowIds.Contains(Guid.Empty)
            || WorkflowIds.Distinct().Count() != WorkflowIds.Count
            || WorkflowAccess is not ("none" or "read" or "run" or "manage")
            || DataAccess is not ("none" or "read" or "manage")
            || TableAccess is not (null or "none" or "read")
            || SourceAccess is not (null or "none" or "read" or "manage")
            || SourceCapabilities is not null && (SourceCapabilities.Count > 4
                || SourceCapabilities.Any(c => c is not ("metadata" or "search" or "connect" or "pipeline"))
                || SourceCapabilities.Distinct().Count() != SourceCapabilities.Count)
            || ArtifactAccess is not (null or "none" or "read"))
            throw new ArgumentException("Use a name up to 60 characters, instructions up to 8,000, valid access levels, up to 200 distinct workflows, and up to 32 distinct MCP connections.");
    }
}
