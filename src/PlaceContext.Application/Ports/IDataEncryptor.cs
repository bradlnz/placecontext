namespace PlaceContext.Application.Ports;

/// <summary>
/// Application-level field encryption for data at rest. Ciphertext is only decryptable by Host
/// processes that hold the Data Protection key ring (and optional <c>PlaceContext:DataProtection:Key</c>
/// envelope). Raw Postgres / MinIO access therefore sees unreadable blobs; the portal and job pipeline
/// decrypt only in-process when serving UI or injecting into sandboxes.
/// </summary>
public interface IDataEncryptor
{
    /// <summary>Purpose segments for isolated key derivation (vault ≠ job source ≠ object store).</summary>
    static class Purpose
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
        /// <summary>Persisted step details and outputs for job-chain executions.</summary>
        public const string ChainRun = "job.chain.run.v1";
        public const string Chart = "project.chart.v1";
        /// <summary>Source text stored with embedding rows (vectors stay searchable in pgvector).</summary>
        public const string EmbeddingText = "content.embedding.text.v1";
        public const string EmailTwoFactorState = "auth.email-2fa.state.v1";
        /// <summary>Customer identity and contact fields stored on CRM client rows.</summary>
        public const string CrmClient = "crm.client.v1";
        public const string CrmCommunication = "crm.communication.v1";
        public const string CrmAppointment = "crm.appointment.v1";
        /// <summary>Customer-facing filenames and storage references on CRM artifact rows.</summary>
        public const string CrmArtifactMetadata = "crm.artifact.metadata.v1";
        public const string CrmAutomation = "crm.automation.v1";
        public const string CrmAutomationPayload = "crm.automation.payload.v1";
    }

    /// <summary>True when <paramref name="value"/> is already in the protected wire format.</summary>
    bool IsProtected(string? value);

    /// <summary>Encrypt for storage. Empty/null and already-protected values pass through.</summary>
    string Protect(string? plaintext, string purpose);

    /// <summary>
    /// Decrypt for portal/job use. Legacy plaintext (no prefix) is returned unchanged so existing
    /// rows keep working until rewritten.
    /// </summary>
    string Unprotect(string? ciphertext, string purpose);

    byte[] ProtectBytes(byte[] plaintext, string purpose);
    byte[] UnprotectBytes(byte[] ciphertext, string purpose);
}
