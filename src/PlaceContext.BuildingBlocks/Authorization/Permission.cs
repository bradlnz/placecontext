namespace PlaceContext.Application.Ports;

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
    public const string CrmView = "crm.view";
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
    public const string CrmCommsSend = "crm.comms.send";
    public const string EmailSend = "email.send";
    public const string SmsSend = "sms.send";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ProjectsView, ProjectsManage, JobsView, JobsEdit, JobsRun, JobsReplay, JobsManage,
        ChainsManage, TriggersManage, CrmView, DataRead, DataWrite, ArtifactsView, ArtifactsShare,
        ArtifactsDelete, SecretsManage, BackupManage, MembersManage, SettingsManage, EventsManage,
        AgentsChat, AgentsManage, CrmCommsSend, EmailSend, SmsSend,
    };
}
