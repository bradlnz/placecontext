namespace PlaceContext.Application.Ports;

/// <summary>
/// The fine-grained permission catalog: every sensitive capability the product actually exposes,
/// curated (not exhaustive) from the MCP tool surface, the Razor pages, and the facade methods that
/// mutate state. A permission is just a stable string — it doubles as the name of the ASP.NET Core
/// authorization policy that enforces it (see Program.cs), so <c>[Authorize(Policy = Permission.X)]</c>
/// is the pattern to follow when gating a new sensitive tool/endpoint/page.
/// </summary>
public static class Permission
{
    public const string ProjectsView = "projects.view";
    public const string ProjectsManage = "projects.manage";
    public const string JobsView = "jobs.view";
    public const string JobsEdit = "jobs.edit";
    public const string JobsRun = "jobs.run";
    public const string JobsReplay = "jobs.replay";
    public const string JobsManage = "jobs.manage";
    public const string ChainsManage = "chains.manage";
    public const string TriggersManage = "triggers.manage";
    public const string DataRead = "data.read";
    public const string DataWrite = "data.write";
    public const string ArtifactsView = "artifacts.view";
    public const string ArtifactsShare = "artifacts.share";
    public const string ArtifactsDelete = "artifacts.delete";
    public const string SecretsManage = "secrets.manage";
    public const string BackupManage = "backup.manage";
    public const string MembersManage = "members.manage";
    public const string SettingsManage = "settings.manage";
    public const string EventsManage = "events.manage";
    public const string AgentsChat = "agents.chat";
    public const string AgentsManage = "agents.manage";
    public const string EmailSend = "email.send";
    public const string SmsSend = "sms.send";

    /// <summary>The whole catalog, in declaration order — drives the admin UI's matrix and policy registration.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ProjectsView, ProjectsManage, JobsView, JobsEdit, JobsRun, JobsReplay, JobsManage,
        ChainsManage, TriggersManage,
        DataRead, DataWrite, ArtifactsView, ArtifactsShare, ArtifactsDelete,
        SecretsManage, BackupManage, MembersManage, SettingsManage, EventsManage,
        AgentsChat, AgentsManage, EmailSend, SmsSend,
    };
}
