import type { McpConnection } from '../../model/mcp'

interface McpServerListProps {
  busy: boolean
  connections: McpConnection[]
  projectSelected: boolean
  onAuthorize: (id: string) => Promise<void>
  onDelete: (id: string) => Promise<void>
  onTest: (id: string) => Promise<void>
}

function isExpired(connection: McpConnection): boolean {
  return (
    connection.oAuthTokenExpiresAt !== null &&
    new Date(connection.oAuthTokenExpiresAt).getTime() <= Date.now()
  )
}

export function McpServerList({
  busy,
  connections,
  projectSelected,
  onAuthorize,
  onDelete,
  onTest,
}: McpServerListProps) {
  if (!projectSelected)
    return <div className="empty-note">Choose a project to manage its MCP servers.</div>
  if (connections.length === 0)
    return <div className="empty-note">No MCP servers configured for this project.</div>

  return connections.map((connection) => {
    const expired = isExpired(connection)
    const oauthConnected = connection.lastStatus?.startsWith('oauth:connected') === true
    return (
      <article
        className={`mcp-server-row${connection.enabled ? '' : ' disabled'}`}
        key={connection.id}
      >
        <div className="mcp-server-info">
          <strong>{connection.name}</strong>
          <span>{connection.transport}</span>
          {connection.authType === 'oauth' ? (
            <span>{expired ? 'OAuth expired' : oauthConnected ? 'OAuth connected' : 'OAuth'}</span>
          ) : null}
          <small>{connection.lastStatus}</small>
        </div>
        <div className="mcp-server-actions">
          {connection.authType === 'oauth' ? (
            <button
              className="dcbtn"
              disabled={busy}
              onClick={() => void onAuthorize(connection.id)}
              type="button"
            >
              {expired ? 'Reconnect' : 'Authorize'}
            </button>
          ) : null}
          <button
            className="dcbtn"
            disabled={busy}
            onClick={() => void onTest(connection.id)}
            type="button"
          >
            Test
          </button>
          <button
            className="dcbtn danger"
            disabled={busy}
            onClick={() => void onDelete(connection.id)}
            type="button"
          >
            Delete
          </button>
        </div>
      </article>
    )
  })
}
