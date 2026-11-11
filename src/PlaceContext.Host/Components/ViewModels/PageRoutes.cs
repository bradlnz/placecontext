namespace PlaceContext.Host.Components.ViewModels;

public static class PageRoutes
{
    public const string GettingStartedWiki = "/wiki/getting-started";
    public const string Wiki = "/wiki";
    public const string DatabaseSettings = "/settings/database";
    public const string ClusterSettings = "/settings/cluster";
    public const string Login = "/login";
    public const string LocalitySettings = "/settings/locality";
    public const string ConnectionsSettings = "/settings/connections";
    public const string McpSettings = "/settings/mcp";
    public static string WebhookIngestionWiki => WikiArticle("webhook-ingestion");

    public static string ProjectJobs(Guid projectId) => $"/project/{projectId}/jobs";
    public static string ProjectWorkflows(Guid projectId) => $"/project/{projectId}/workflows";
    public static string DashboardRun(Guid projectId, Guid runId, Guid? submissionId = null) =>
        $"/project/{projectId}/dashboard/runs/{runId}" + (submissionId is { } id ? $"/submission/{id}" : "");
    public static string ApplicationDatabase(Guid projectId) => $"{ProjectData(projectId)}?database=application";
    public static string ProjectData(Guid projectId) => $"/project/{projectId}/data";

    public static string ProjectTests(Guid projectId) => $"/project/{projectId}/tests";

    public static string ProjectSecrets(Guid projectId) => $"/project/{projectId}/secrets";

    public static string ProjectJob(Guid projectId, Guid jobId) =>
        $"{ProjectJobs(projectId)}/{jobId}";

    public static string WikiArticle(string slug) => $"{Wiki}/{slug}";
}
