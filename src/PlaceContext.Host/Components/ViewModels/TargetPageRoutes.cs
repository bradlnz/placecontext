namespace PlaceContext.Host.Components.ViewModels;

public static class PageRoutes
{
    public const string GettingStartedWiki = "/wiki/getting-started";
    public const string Wiki = "/wiki";
    public const string Login = "/login";
    public const string LocalitySettings = "/settings/locality";
    public const string ConnectionsSettings = "/settings/connections";
    public const string McpSettings = "/settings/mcp";
    public const string AgentsSettings = "/settings/agents";
    public static string WebhookIngestionWiki => WikiArticle("webhook-ingestion");

    public static string ProjectJobs(Guid projectId) => $"/project/{projectId}/jobs";
    public static string ProjectDataJobs(Guid projectId) => $"/project/{projectId}/data-jobs";

    public static string ProjectData(Guid projectId) => $"/project/{projectId}/data";

    public static string ProjectTests(Guid projectId) => $"/project/{projectId}/tests";

    public static string ProjectAgents(Guid projectId) => $"/project/{projectId}/agents";

    public static string ProjectAgentGoals(Guid projectId) => $"/project/{projectId}/agents?tab=work";

    public static string ProjectAgentChat(Guid projectId) => $"/project/{projectId}/agent-chat";

    public static string ProjectSecrets(Guid projectId) => $"/project/{projectId}/secrets";

    public static string ProjectJob(Guid projectId, Guid jobId) =>
        $"{ProjectJobs(projectId)}/{jobId}";

    public static string WikiArticle(string slug) => $"{Wiki}/{slug}";
}
