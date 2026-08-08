import type { McpConnectionDraft, McpAuthType, McpTransport } from '../../model/mcp'

interface McpServerFormProps {
  busy: boolean
  draft: McpConnectionDraft
  onCancel: () => Promise<void>
  onChange: (draft: McpConnectionDraft) => Promise<void>
  onSave: () => Promise<void>
}

export function McpServerForm({ busy, draft, onCancel, onChange, onSave }: McpServerFormProps) {
  const isStdio = draft.transport === 'stdio'
  const hasTokenAuth = draft.authType === 'bearer' || draft.authType === 'apikey'
  const isHeaderAuth = draft.authType === 'header'
  const isOAuth = draft.authType === 'oauth'

  async function patch(update: Partial<McpConnectionDraft>): Promise<void> {
    await onChange({ ...draft, ...update })
  }

  return (
    <section className="dccard mcp-add-card">
      <h2>New MCP server</h2>
      <div className="settings-field-grid two-columns">
        <label className="dcfield">
          <span>Name</span>
          <input
            onChange={(event) => void patch({ name: event.target.value })}
            placeholder="Property tools"
            value={draft.name}
          />
        </label>
        <label className="dcfield">
          <span>Transport</span>
          <select
            onChange={(event) => void patch({ transport: event.target.value as McpTransport })}
            value={draft.transport}
          >
            <option value="http">HTTP</option>
            <option value="sse">SSE</option>
            <option value="stdio">Stdio</option>
          </select>
        </label>
      </div>
      {isStdio ? (
        <div className="settings-field-grid two-columns">
          <label className="dcfield">
            <span>Command</span>
            <input
              onChange={(event) => void patch({ command: event.target.value })}
              placeholder="npx"
              value={draft.command}
            />
          </label>
          <label className="dcfield">
            <span>Arguments</span>
            <input
              onChange={(event) => void patch({ args: event.target.value })}
              placeholder='["-y", "server"]'
              value={draft.args}
            />
          </label>
        </div>
      ) : (
        <label className="dcfield">
          <span>Endpoint</span>
          <input
            onChange={(event) => void patch({ endpointUrl: event.target.value })}
            placeholder="https://mcp.example.com/mcp"
            value={draft.endpointUrl}
          />
        </label>
      )}
      <div className="settings-field-grid two-columns">
        <label className="dcfield">
          <span>Authentication</span>
          <select
            onChange={(event) => void patch({ authType: event.target.value as McpAuthType })}
            value={draft.authType}
          >
            <option value="none">None</option>
            <option value="bearer">Bearer token</option>
            <option value="apikey">API key</option>
            <option value="header">Custom header</option>
            <option value="oauth">OAuth 2.1</option>
          </select>
        </label>
        {hasTokenAuth ? (
          <label className="dcfield">
            <span>Token</span>
            <input
              autoComplete="new-password"
              onChange={(event) => void patch({ authToken: event.target.value })}
              type="password"
              value={draft.authToken}
            />
          </label>
        ) : null}
        {isHeaderAuth ? (
          <label className="dcfield">
            <span>Header name</span>
            <input
              onChange={(event) => void patch({ authHeader: event.target.value })}
              placeholder="X-API-Key"
              value={draft.authHeader}
            />
          </label>
        ) : null}
      </div>
      {isHeaderAuth ? (
        <label className="dcfield">
          <span>Header value</span>
          <input
            autoComplete="new-password"
            onChange={(event) => void patch({ authToken: event.target.value })}
            type="password"
            value={draft.authToken}
          />
        </label>
      ) : null}
      {isOAuth ? (
        <label className="dcfield">
          <span>OAuth scopes</span>
          <input
            onChange={(event) => void patch({ oAuthScopes: event.target.value })}
            placeholder="openid profile"
            value={draft.oAuthScopes}
          />
        </label>
      ) : null}
      <div className="settings-actions">
        <button className="dcbtn" disabled={busy} onClick={() => void onCancel()} type="button">
          Cancel
        </button>
        <button
          className="dcbtn primary"
          disabled={busy || draft.name.trim() === ''}
          onClick={() => void onSave()}
          type="button"
        >
          {busy ? 'Adding…' : 'Add server'}
        </button>
      </div>
    </section>
  )
}
