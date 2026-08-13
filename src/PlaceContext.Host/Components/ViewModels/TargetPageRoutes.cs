namespace PlaceContext.Host.Components.ViewModels;

public static class PageRoutes
{
    public const string GettingStartedWiki = "/wiki/getting-started";
    public const string Wiki = "/wiki";
    public const string Login = "/login";
    public const string LocalitySettings = "/settings/locality";

    public static string ProjectJobs(Guid projectId) => $"/project/{projectId}/jobs";
    public static string ProjectDataJobs(Guid projectId) => $"/project/{projectId}/data-jobs";

    public static string ProjectTests(Guid projectId) => $"/project/{projectId}/tests";

    public static string ProjectSecrets(Guid projectId) => $"/project/{projectId}/secrets";

    public static string ProjectAgents(Guid projectId) => $"/project/{projectId}/agents";

    public static string ProjectJob(Guid projectId, Guid jobId) =>
        $"{ProjectJobs(projectId)}/{jobId}";

    public static string WikiArticle(string slug) => $"{Wiki}/{slug}";
}
