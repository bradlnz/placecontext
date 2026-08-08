import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { createMcpConnection, deleteMcpConnection, testMcpConnection } from '../../api/mcp-api'
import { mcpSettingsQueryOptions } from '../../api/mcp-query'
import type { CreateMcpConnectionInput, McpConnection, McpConnectionDraft } from '../../model/mcp'
import { McpServerForm } from './McpServerForm'
import { McpServerList } from './McpServerList'

const EMPTY_DRAFT: McpConnectionDraft = {
  name: '',
  transport: 'http',
  endpointUrl: '',
  command: '',
  args: '',
  authType: 'none',
  authToken: '',
  authHeader: '',
  oAuthScopes: '',
}

type McpMutation =
  | { kind: 'create'; projectId: string; input: CreateMcpConnectionInput }
  | { kind: 'delete'; projectId: string; connectionId: string }
  | { kind: 'test'; projectId: string; connectionId: string }

function toInput(draft: McpConnectionDraft): CreateMcpConnectionInput {
  const isStdio = draft.transport === 'stdio'
  return {
    name: draft.name.trim(),
    transport: draft.transport,
    endpointUrl: isStdio ? null : draft.endpointUrl.trim(),
    command: isStdio ? draft.command.trim() : null,
    args: isStdio ? draft.args.trim() : null,
    authType: draft.authType,
    authToken: draft.authType === 'none' || draft.authType === 'oauth' ? null : draft.authToken,
    authHeader: draft.authType === 'header' ? draft.authHeader.trim() : null,
    oAuthScopes: draft.authType === 'oauth' ? draft.oAuthScopes.trim() : null,
  }
}

export function McpSettingsPage() {
  const [selectedProjectId, setSelectedProjectId] = useState<string | null | undefined>(undefined)
  const queryOptions = mcpSettingsQueryOptions(
    typeof selectedProjectId === 'string' ? selectedProjectId : undefined,
  )
  const { data } = useSuspenseQuery(queryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [showAdd, setShowAdd] = useState(false)
  const [draft, setDraft] = useState<McpConnectionDraft>(EMPTY_DRAFT)
  const [message, setMessage] = useState<string | null>(null)
  const projectId = selectedProjectId === undefined ? data.projectId : selectedProjectId
  const connections = projectId === null ? [] : data.connections

  const mutation = useMutation({
    mutationFn: async (command: McpMutation): Promise<McpConnection | null> => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'create')
        return createMcpConnection(command.projectId, command.input, signal)
      if (command.kind === 'test') return testMcpConnection(command.connectionId, signal)
      await deleteMcpConnection(command.connectionId, signal)
      return null
    },
    onSuccess: async (connection, command) => {
      setShowAdd(false)
      setDraft(EMPTY_DRAFT)
      if (command.kind === 'create') setMessage('MCP server added.')
      if (command.kind === 'delete') setMessage('MCP server deleted.')
      if (command.kind === 'test')
        setMessage(`${connection?.name ?? 'MCP server'}: ${connection?.lastStatus ?? 'unknown'}`)
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: mcpSettingsQueryOptions(command.projectId).queryKey,
        }),
        eventBus.publish('settings.mcp-connections-changed', {
          projectId: command.projectId,
        }),
      ])
    },
  })

  useEffect(() => {
    function handleOAuthMessage(event: MessageEvent<unknown>): void {
      if (event.origin !== window.location.origin) return
      if (typeof event.data !== 'string' || !event.data.startsWith('mcp-oauth-')) return
      void queryClient.invalidateQueries({
        queryKey: mcpSettingsQueryOptions(typeof projectId === 'string' ? projectId : undefined)
          .queryKey,
      })
    }
    window.addEventListener('message', handleOAuthMessage)
    return () => {
      window.removeEventListener('message', handleOAuthMessage)
    }
  }, [projectId, queryClient])

  async function execute(command: McpMutation): Promise<void> {
    setMessage(null)
    try {
      await mutation.mutateAsync(command)
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'MCP server action failed.')
    }
  }

  async function selectProject(value: string | null): Promise<void> {
    await Promise.resolve()
    setSelectedProjectId(value)
    setShowAdd(false)
    setMessage(null)
  }

  async function openAdd(): Promise<void> {
    await Promise.resolve()
    setDraft(EMPTY_DRAFT)
    setShowAdd(true)
  }

  async function closeAdd(): Promise<void> {
    await Promise.resolve()
    setShowAdd(false)
  }

  async function changeDraft(value: McpConnectionDraft): Promise<void> {
    await Promise.resolve()
    setDraft(value)
  }

  async function saveDraft(): Promise<void> {
    if (projectId === null) return
    await execute({ kind: 'create', projectId, input: toInput(draft) })
  }

  async function authorize(connectionId: string): Promise<void> {
    await Promise.resolve()
    window.open(`/mcp-oauth/start?connectionId=${encodeURIComponent(connectionId)}`, '_blank')
  }

  return (
    <div className="settings-page mcp-settings-page">
      <title>PlaceContext — MCP servers</title>
      <header className="settings-page-head">
        <div>
          <span className="settings-kicker">Project integrations</span>
          <h1>MCP servers</h1>
          <p>
            Connect external tool servers and make them available to jobs and agents in the selected
            project.
          </p>
        </div>
        <button
          className="dcbtn primary"
          disabled={projectId === null}
          onClick={() => void openAdd()}
          type="button"
        >
          + Add server
        </button>
      </header>
      <label className="dcfield mcp-project-picker">
        <span>Project</span>
        <select
          onChange={(event) => void selectProject(event.target.value || null)}
          value={projectId ?? ''}
        >
          <option value="">Select a project…</option>
          {data.projects.map((project) => (
            <option key={project.id} value={project.id}>
              {project.name}
            </option>
          ))}
        </select>
      </label>
      {message === null ? null : (
        <div className="settings-message" role="status">
          {message}
        </div>
      )}
      <section className="dccard mcp-server-card">
        <McpServerList
          busy={mutation.isPending}
          connections={connections}
          onAuthorize={authorize}
          onDelete={async (connectionId) => {
            if (projectId !== null) await execute({ kind: 'delete', projectId, connectionId })
          }}
          onTest={async (connectionId) => {
            if (projectId !== null) await execute({ kind: 'test', projectId, connectionId })
          }}
          projectSelected={projectId !== null}
        />
      </section>
      {showAdd && projectId !== null ? (
        <McpServerForm
          busy={mutation.isPending}
          draft={draft}
          onCancel={closeAdd}
          onChange={changeDraft}
          onSave={saveDraft}
        />
      ) : null}
    </div>
  )
}
