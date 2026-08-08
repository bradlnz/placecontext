import { useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { inspectorToolCallsQuery } from '../../api/inspector-query-options'
import type { InspectorToolCall } from '../../model/inspector'

type InspectorStatus = 'ok' | 'warn' | 'bad'

function statusKind(status: string): InspectorStatus {
  if (status === 'Ok') return 'ok'
  if (status === 'Warn') return 'warn'
  return 'bad'
}

function statusLabel(status: string): string {
  return status.toLocaleLowerCase()
}

export function InspectorPage() {
  const { data: calls, isFetching, refetch } = useSuspenseQuery(inspectorToolCallsQuery)
  const [selectedCallId, setSelectedCallId] = useState<string | null>(null)
  const activeCall = calls.find((call) => call.id === selectedCallId) ?? calls[0] ?? null

  async function refresh(): Promise<void> {
    await refetch()
  }

  async function selectCall(call: InspectorToolCall): Promise<void> {
    await Promise.resolve()
    setSelectedCallId(call.id)
  }

  return (
    <div className="inspector-shell">
      <title>PlaceContext — MCP Inspector</title>
      <section className="inspector-feed" aria-label="Tool calls">
        <header className="inspector-feed-head">
          <div className="inspector-heading">
            <strong>Tool calls</strong>
            <span className="inspector-pulse" aria-label="Live updates active" />
          </div>
          <button
            className="dcbtn inspector-refresh"
            disabled={isFetching}
            onClick={() => void refresh()}
            type="button"
          >
            {isFetching ? 'refreshing…' : 'refresh'}
          </button>
        </header>
        <div className="inspector-feed-list">
          {calls.length === 0 ? (
            <div className="inspector-feed-empty">
              No tool calls yet. Launch the MCP server (<code>--mcp</code>) and call a tool, or use
              the portal — calls appear here live.
            </div>
          ) : (
            calls.map((call) => {
              const active = activeCall?.id === call.id
              const kind = statusKind(call.status)
              return (
                <button
                  aria-pressed={active}
                  className={active ? 'inspector-call active' : 'inspector-call'}
                  key={call.id}
                  onClick={() => void selectCall(call)}
                  type="button"
                >
                  <span className={`inspector-status-dot ${kind}`} />
                  <span className="inspector-call-content">
                    <span className="inspector-call-title">
                      <strong>{call.tool}</strong>
                      <small>{call.direction}</small>
                    </span>
                    <span className="inspector-call-summary">{call.summary}</span>
                  </span>
                  <span className="inspector-call-meta">
                    <strong className={kind}>{statusLabel(call.status)}</strong>
                    <small>{call.durationMs} ms</small>
                  </span>
                </button>
              )
            })
          )}
        </div>
      </section>
      <section className="inspector-detail" aria-label="Tool call detail">
        {activeCall === null ? (
          <div className="inspector-detail-empty">
            Select a tool call to inspect its request and response.
          </div>
        ) : (
          <>
            <header className="inspector-detail-head">
              <strong>{activeCall.tool}</strong>
              <span className={`inspector-status-pill ${statusKind(activeCall.status)}`}>
                {statusLabel(activeCall.status)}
              </span>
              <small>
                {activeCall.durationMs} ms · {activeCall.project}
              </small>
            </header>
            <div className="inspector-detail-body">
              <div>
                <h2>Request</h2>
                <pre>{activeCall.requestJson}</pre>
              </div>
              <div>
                <h2>Response</h2>
                <pre>{activeCall.responseJson}</pre>
              </div>
            </div>
          </>
        )}
      </section>
    </div>
  )
}
