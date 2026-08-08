import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { resetExternalDatabase, resetExternalIndex, saveExternalDatabase, saveExternalIndex } from '../../api/connections-api'
import { connectionsQueryOptions } from '../../api/connections-query'
import type { ConnectionProject, ConnectionsSettings, ExternalDatabaseInput, ExternalIndexInput } from '../../model/connections'

const EMPTY_DATABASE: ExternalDatabaseInput = {
  host: '',
  port: '',
  database: '',
  username: '',
  password: '',
  sslMode: 'Prefer',
}

const EMPTY_INDEX: ExternalIndexInput = {
  endpoint: '',
  username: '',
  password: '',
  index: '',
}

type ConnectionMutation =
  | { kind: 'save-database'; projectId: string; input: ExternalDatabaseInput }
  | { kind: 'reset-database'; projectId: string }
  | { kind: 'save-index'; projectId: string; input: ExternalIndexInput }
  | { kind: 'reset-index'; projectId: string }

function updateProject(settings: ConnectionsSettings | undefined, project: ConnectionProject): ConnectionsSettings | undefined {
  if (settings === undefined) return undefined
  return {
    ...settings,
    projects: settings.projects.map((current) => current.id === project.id ? project : current),
  }
}

export function ConnectionsPage() {
  const { data } = useSuspenseQuery(connectionsQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(() => data.projects[0]?.id ?? null)
  const [database, setDatabase] = useState<ExternalDatabaseInput>(EMPTY_DATABASE)
  const [index, setIndex] = useState<ExternalIndexInput>(EMPTY_INDEX)
  const [message, setMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const selectedProject = data.projects.find(({ id }) => id === selectedProjectId) ?? null

  const mutation = useMutation({
    mutationFn: async (command: ConnectionMutation): Promise<ConnectionProject> => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'save-database') return saveExternalDatabase(command.projectId, command.input, signal)
      if (command.kind === 'reset-database') return resetExternalDatabase(command.projectId, signal)
      if (command.kind === 'save-index') return saveExternalIndex(command.projectId, command.input, signal)
      return resetExternalIndex(command.projectId, signal)
    },
    onSuccess: async (project, command) => {
      queryClient.setQueryData<ConnectionsSettings>(connectionsQueryOptions.queryKey, (current) => updateProject(current, project))
      setDatabase(EMPTY_DATABASE)
      setIndex(EMPTY_INDEX)
      setFormError(null)
      if (command.kind === 'save-database') setMessage(`External database for '${project.name}' saved. This project now runs against it; reset to return to the cluster database.`)
      if (command.kind === 'reset-database') setMessage(`Project '${project.name}' reverted to the cluster database.`)
      if (command.kind === 'save-index') setMessage(`External index for '${project.name}' saved. Data Search now targets it; reset to fall back to the workspace default.`)
      if (command.kind === 'reset-index') setMessage(`Project '${project.name}' reverted to the workspace OpenSearch default.`)
      await eventBus.publish('settings.connections-changed', { projectId: project.id })
    },
  })

  async function execute(command: ConnectionMutation): Promise<void> {
    setMessage(null)
    try {
      await mutation.mutateAsync(command)
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'Connection settings could not be updated.')
    }
  }

  async function selectProject(projectId: string | null): Promise<void> {
    await Promise.resolve()
    setSelectedProjectId(projectId)
    setDatabase(EMPTY_DATABASE)
    setIndex(EMPTY_INDEX)
    setFormError(null)
    setMessage(null)
  }

  async function submitDatabase(): Promise<void> {
    setFormError(null)
    if (selectedProjectId === null) { setFormError('Select a project first.'); return }
    if (database.host.trim() === '') { setFormError('Host is required.'); return }
    if (database.username.trim() === '') { setFormError('Username is required.'); return }
    if (database.password === '') { setFormError('Password is required.'); return }
    if (database.port.trim() !== '' && !/^\d+$/.test(database.port.trim())) { setFormError('Port must be a number.'); return }
    if (!data.sslModes.some((mode) => mode.toLowerCase() === database.sslMode.toLowerCase())) { setFormError('Invalid SSL mode.'); return }
    await execute({ kind: 'save-database', projectId: selectedProjectId, input: database })
  }

  async function submitIndex(): Promise<void> {
    setFormError(null)
    if (selectedProjectId === null) { setFormError('Select a project first.'); return }
    if (index.endpoint.trim() === '') { setFormError('Endpoint is required.'); return }
    try {
      const url = new URL(index.endpoint.trim())
      if (url.protocol !== 'http:' && url.protocol !== 'https:') throw new Error()
    } catch {
      setFormError('Endpoint must be an absolute HTTP or HTTPS URL.')
      return
    }
    await execute({ kind: 'save-index', projectId: selectedProjectId, input: index })
  }

  return (
    <div className="settings-page connections-page">
      <title>placecontext — Connections</title>
      <h1>Connections</h1>
      <p className="settings-intro">What each project's data runs against. Every project uses the shared cluster database and the workspace OpenSearch default until you connect an external one here. Credentials are stored encrypted in the project's Vault and never displayed after save.</p>
      {message === null ? null : <div className="settings-message" role="status">{message}</div>}
      <section className="dccard connection-card">
        <label className="dcfield connection-project-select">
          <span>Project</span>
          <select onChange={(event) => void selectProject(event.target.value || null)} value={selectedProjectId ?? ''}>
            {data.projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}
          </select>
        </label>
        <div className="connection-hint">
          {selectedProject === null ? <span>Select a project to configure its connections.</span> : <span>{selectedProject.name} — {selectedProject.hasExternalDatabase ? 'external database configured' : 'using the cluster database'} · {selectedProject.hasExternalIndex ? 'external search index configured' : 'using the workspace OpenSearch default'}</span>}
        </div>
      </section>
      {selectedProject === null ? null : (
        <>
          <section className="dccard connection-card">
            <header className="connection-card-head">
              <h2>External database</h2>
              {selectedProject.hasExternalDatabase ? <button className="dcbtn" disabled={mutation.isPending} onClick={() => void execute({ kind: 'reset-database', projectId: selectedProject.id })} type="button">Reset to cluster database</button> : null}
            </header>
            <p>Connect the project to its own Postgres. SQL Studio and the Records tab then run against it; DDL and DML are allowed as the configured user. Leave blank to keep the cluster database.</p>
            <div className="connection-grid">
              <label className="dcfield"><span>Host</span><input onChange={(event) => { setDatabase((current) => ({ ...current, host: event.target.value })) }} placeholder="db.example.com" value={database.host} /></label>
              <label className="dcfield connection-small"><span>Port</span><input inputMode="numeric" onChange={(event) => { setDatabase((current) => ({ ...current, port: event.target.value })) }} placeholder="5432" value={database.port} /></label>
              <label className="dcfield"><span>Database</span><input onChange={(event) => { setDatabase((current) => ({ ...current, database: event.target.value })) }} placeholder="defaults to username" value={database.database} /></label>
              <label className="dcfield"><span>Username</span><input autoComplete="off" onChange={(event) => { setDatabase((current) => ({ ...current, username: event.target.value })) }} placeholder="postgres" value={database.username} /></label>
              <label className="dcfield"><span>Password</span><input autoComplete="new-password" onChange={(event) => { setDatabase((current) => ({ ...current, password: event.target.value })) }} placeholder="••••••••" type="password" value={database.password} /></label>
              <label className="dcfield connection-small"><span>SSL mode</span><select onChange={(event) => { setDatabase((current) => ({ ...current, sslMode: event.target.value })) }} value={database.sslMode}>{data.sslModes.map((mode) => <option key={mode} value={mode}>{mode}</option>)}</select></label>
            </div>
            <button className="dcbtn primary" disabled={mutation.isPending} onClick={() => void submitDatabase()} type="button">{mutation.isPending ? 'Saving…' : 'Save external database'}</button>
            {formError === null ? null : <div className="connection-error" role="alert">{formError}</div>}
          </section>
          <section className="dccard connection-card">
            <header className="connection-card-head">
              <h2>External search index</h2>
              {selectedProject.hasExternalIndex ? <button className="dcbtn" disabled={mutation.isPending} onClick={() => void execute({ kind: 'reset-index', projectId: selectedProject.id })} type="button">Reset to workspace default</button> : null}
            </header>
            <p>Point this project's Data Search at an external OpenSearch / Elasticsearch endpoint. Leave blank to keep the workspace default.</p>
            <div className="connection-grid">
              <label className="dcfield connection-wide"><span>Endpoint</span><input onChange={(event) => { setIndex((current) => ({ ...current, endpoint: event.target.value })) }} placeholder="https://search.example.com" value={index.endpoint} /></label>
              <label className="dcfield"><span>Username</span><input autoComplete="off" onChange={(event) => { setIndex((current) => ({ ...current, username: event.target.value })) }} placeholder="optional" value={index.username} /></label>
              <label className="dcfield"><span>Password</span><input autoComplete="new-password" onChange={(event) => { setIndex((current) => ({ ...current, password: event.target.value })) }} placeholder="optional" type="password" value={index.password} /></label>
              <label className="dcfield"><span>Index</span><input onChange={(event) => { setIndex((current) => ({ ...current, index: event.target.value })) }} placeholder="*" value={index.index} /></label>
            </div>
            <button className="dcbtn primary" disabled={mutation.isPending} onClick={() => void submitIndex()} type="button">{mutation.isPending ? 'Saving…' : 'Save external index'}</button>
            {formError === null ? null : <div className="connection-error" role="alert">{formError}</div>}
          </section>
        </>
      )}
    </div>
  )
}
