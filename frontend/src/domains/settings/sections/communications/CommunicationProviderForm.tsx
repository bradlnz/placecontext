import type { CommunicationProject, CommunicationProviderDraft, CommunicationSecret } from '../../model/communications'

interface CommunicationProviderFormProps {
  busy: boolean
  draft: CommunicationProviderDraft
  editing: boolean
  projects: CommunicationProject[]
  secrets: CommunicationSecret[]
  onCancel: () => Promise<void>
  onChange: (draft: CommunicationProviderDraft) => Promise<void>
  onProjectChange: (projectId: string | null) => Promise<void>
  onSave: () => Promise<void>
}

function defaultHeaderName(kind: CommunicationProviderDraft['kind']): string {
  return kind === 'postmark' ? 'X-Postmark-Server-Token' : ''
}

export function CommunicationProviderForm({ busy, draft, editing, projects, secrets, onCancel, onChange, onProjectChange, onSave }: CommunicationProviderFormProps) {
  const kinds = draft.channel === 'sms' ? ['twilio'] as const : ['postmark', 'sendgrid'] as const
  const usesSecret = draft.authType !== 'none'

  async function patch(update: Partial<CommunicationProviderDraft>): Promise<void> {
    await onChange({ ...draft, ...update })
  }

  async function changeChannel(channel: CommunicationProviderDraft['channel']): Promise<void> {
    const kind = channel === 'sms' ? 'twilio' : 'postmark'
    await patch({
      channel,
      kind,
      ...(draft.authType === 'header' ? { authHeaderName: defaultHeaderName(kind) } : {}),
    })
  }

  async function changeKind(kind: CommunicationProviderDraft['kind']): Promise<void> {
    await patch({
      kind,
      ...(draft.authType === 'header' ? { authHeaderName: defaultHeaderName(kind) } : {}),
    })
  }

  async function changeAuthType(authType: CommunicationProviderDraft['authType']): Promise<void> {
    await patch({
      authType,
      ...(authType === 'header' && (draft.authHeaderName ?? '').trim() === ''
        ? { authHeaderName: defaultHeaderName(draft.kind) }
        : {}),
    })
  }

  return (
    <section className="dccard communication-form">
      <h2>{editing ? `Edit ${draft.name}` : 'New provider'}</h2>
      <div className="settings-field-grid two-columns">
        <label className="dcfield">
          <span>Channel</span>
          <select onChange={(event) => void changeChannel(event.target.value as CommunicationProviderDraft['channel'])} value={draft.channel}>
            <option value="email">Email</option>
            <option value="sms">SMS</option>
          </select>
        </label>
        <label className="dcfield">
          <span>Provider kind</span>
          <select onChange={(event) => void changeKind(event.target.value as CommunicationProviderDraft['kind'])} value={draft.kind}>
            {kinds.map((kind) => <option key={kind} value={kind}>{kind === 'sendgrid' ? 'SendGrid' : kind === 'twilio' ? 'Twilio' : 'Postmark'}</option>)}
          </select>
        </label>
        <label className="dcfield">
          <span>Display name</span>
          <input onChange={(event) => void patch({ name: event.target.value })} placeholder="Transactional email" value={draft.name} />
        </label>
        <label className="dcfield">
          <span>Authentication</span>
          <select onChange={(event) => void changeAuthType(event.target.value as CommunicationProviderDraft['authType'])} value={draft.authType}>
            <option value="none">None</option>
            <option value="bearer">Bearer token</option>
            <option value="header">API key header</option>
            <option value="basic">Basic auth</option>
          </select>
        </label>
      </div>
      <label className="communication-enabled">
        <input checked={draft.enabled} onChange={(event) => void patch({ enabled: event.target.checked })} type="checkbox" /> Enabled
      </label>
      {draft.authType === 'header' ? (
        <label className="dcfield">
          <span>Header name</span>
          <input aria-label="Header name" onChange={(event) => void patch({ authHeaderName: event.target.value })} placeholder="X-Api-Key" value={draft.authHeaderName ?? ''} />
          <small>The header that carries the API key. Postmark uses <code>X-Postmark-Server-Token</code>.</small>
        </label>
      ) : null}
      {draft.authType === 'basic' ? <div className="settings-message">Basic auth takes its username from the Account SID field below; the Vault secret is the auth token used as the password.</div> : null}
      {usesSecret ? (
        <div className="settings-field-grid two-columns">
          <label className="dcfield">
            <span>Vault project</span>
            <select aria-label="Vault project" onChange={(event) => void onProjectChange(event.target.value || null)} value={draft.vaultProjectId ?? ''}>
              <option value="">Select a project…</option>
              {projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}
            </select>
            <small>The project whose Vault contains the API key.</small>
          </label>
          <label className="dcfield">
            <span>API key secret</span>
            <select aria-label="API key secret" disabled={draft.vaultProjectId === null} onChange={(event) => void patch({ apiKeySecretName: event.target.value || null })} value={draft.apiKeySecretName ?? ''}>
              <option value="">Select a Vault secret…</option>
              {secrets.map((secret) => <option key={secret.name} value={secret.name}>{secret.name}</option>)}
            </select>
            {draft.vaultProjectId !== null && secrets.length === 0 ? <small className="warning">No Vault secrets exist in this project yet.</small> : null}
          </label>
        </div>
      ) : null}
      {draft.kind === 'twilio' ? (
        <div className="settings-field-grid two-columns">
          <label className="dcfield">
            <span>Account SID</span>
            <input aria-label="Account SID" onChange={(event) => void patch({ accountSid: event.target.value })} placeholder="AC…" value={draft.accountSid} />
            <small>Also used as the basic-auth username.</small>
          </label>
          <label className="dcfield">
            <span>From number</span>
            <input onChange={(event) => void patch({ fromNumber: event.target.value })} placeholder="+15551234567" value={draft.fromNumber} />
          </label>
        </div>
      ) : (
        <div className="settings-field-grid two-columns">
          <label className="dcfield">
            <span>Verified sender email</span>
            <input aria-label="Verified sender email" onChange={(event) => void patch({ fromEmail: event.target.value })} placeholder="hello@yourdomain.com" type="email" value={draft.fromEmail} />
            <small>This address or its domain must be verified with the provider.</small>
          </label>
          <label className="dcfield">
            <span>Sender name</span>
            <input onChange={(event) => void patch({ fromName: event.target.value })} placeholder="Your company" value={draft.fromName} />
          </label>
          {draft.kind === 'postmark' ? (
            <label className="dcfield">
              <span>Message stream</span>
              <input aria-label="Message stream" onChange={(event) => void patch({ messageStream: event.target.value })} placeholder="outbound" value={draft.messageStream} />
              <small>Use <code>outbound</code> unless you created a dedicated transactional stream.</small>
            </label>
          ) : null}
        </div>
      )}
      <label className="dcfield">
        <span>Endpoint override (optional)</span>
        <input onChange={(event) => void patch({ endpoint: event.target.value })} placeholder="Leave empty for the provider default" value={draft.endpoint} />
      </label>
      <div className="settings-actions">
        <button className="dcbtn" onClick={() => void onCancel()} type="button">Cancel</button>
        <button className="dcbtn primary" disabled={busy || draft.name.trim() === ''} onClick={() => void onSave()} type="button">{busy ? 'Saving…' : editing ? 'Save changes' : 'Add provider'}</button>
      </div>
    </section>
  )
}
