namespace PlaceContext.Application.Ports;

/// <summary>Purpose segments that isolate derived encryption keys by stored data category.</summary>
public static class DataEncryptionPurpose
{
    public const string Vault = "vault.secrets.v1";
    public const string JobSource = "job.source.v1";
    public const string JobRun = "job.run.v1";
    public const string Requirements = "project.requirements.v1";
    public const string EventPayload = "event.payload.v1";
    public const string ProjectData = "project.data.v1";
    public const string ObjectStore = "object.store.v1";
    public const string Decision = "project.decision.v1";
    public const string Activity = "project.activity.v1";
    public const string ToolCall = "mcp.toolcall.v1";
    public const string PendingRun = "job.pending.v1";
    public const string ChainRun = "job.chain.run.v1";
    public const string Chart = "project.chart.v1";
    public const string EmbeddingText = "content.embedding.text.v1";
    public const string EmailTwoFactorState = "auth.email-2fa.state.v1";
    public const string CrmClient = "crm.client.v1";
    public const string CrmCommunication = "crm.communication.v1";
    public const string CrmAppointment = "crm.appointment.v1";
    public const string CrmArtifactMetadata = "crm.artifact.metadata.v1";
    public const string CrmAutomation = "crm.automation.v1";
    public const string CrmAutomationPayload = "crm.automation.payload.v1";
}
