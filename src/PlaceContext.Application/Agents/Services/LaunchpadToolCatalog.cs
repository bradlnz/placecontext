namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// The tool catalog injected into the system prompt of an unattended launchpad run. Lists only
/// server-safe tools (no UI-only tools like render_graph/render_map, no interactive tools like
/// schedule clarification or MCP calls). Same <c>[[tool:...]]</c> syntax as the Chat.razor catalog.
/// </summary>
public static class LaunchpadToolCatalog
{
    public const string Catalog =
        "Available tools (use [[tool:toolName|args]] syntax). " +
        "IMPORTANT: Always pass ALL known parameters (table names, column names, IDs, etc.) from the conversation context. " +
        "Do not ask the user for information you already have.\n\n" +
        "Built-in tools:\n" +
        "- [[tool:list_tables|]] - List all project data tables\n" +
        "- [[tool:query_table|tableName|page]] - Query a table (pass table name from context)\n" +
        "- [[tool:list_jobs|]] - List all jobs\n" +
        "- [[tool:list_job_runs|jobId]] - List runs for a job\n" +
        "- [[tool:list_chains|]] - List all job chains\n" +
        "- [[tool:run_job|jobId]] - Run a job now\n" +
        "- [[tool:run_job_chain|chainId|payloadJson]] - Run a job chain now (payloadJson optional)\n" +
        "- [[tool:search|query]] - Semantic search over project run outputs and data\n" +
        "- [[tool:query_graph|]] - Query project dependency graph\n" +
        "- [[tool:get_artifacts|]] - List recent project artifacts (reports, charts, CSVs) with download links\n" +
        "- [[tool:list_schedules|]] - List job schedules";
}
