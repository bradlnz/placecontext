import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { apiTokensQueryOptions } from '../../api/api-tokens-query'
import { createApiToken, revokeApiToken } from '../../api/api-tokens-api'
import type { CreatedApiToken } from '../../model/api-token'

const ENDPOINTS = [
  ['GET', '/api/v1/entities', 'List entities for the resolved project.'],
  [
    'GET',
    '/api/v1/{entity-name}',
    'List rows for an entity/table (query: search, page, pageSize).',
  ],
  ['GET', '/api/v1/{entity-name}/{key}', 'Look up one entity row by label/first-column key.'],
  ['POST', '/api/v1/{job-name}', 'Run a named job with API invocation enabled.'],
  [
    'POST',
    '/api/v1/{entity-name}/jobs/{jobId}/run',
    'Run a specific job on an entity with API invocation enabled.',
  ],
  ['GET', '/api/v1/search?q=<query>', 'Search within the resolved project.'],
] as const

const EXAMPLES = [
  [
    'List entities',
    'curl -H "Authorization: Bearer $PC_TOKEN" -H "X-Project-Id: $PC_PROJECT_ID" "$PC_HOST/api/v1/entities"',
  ],
  [
    'Query an entity',
    'curl -H "Authorization: Bearer $PC_TOKEN" -H "X-Project: $PC_PROJECT_NAME" "$PC_HOST/api/v1/example-entity?search=acme&page=1&pageSize=20"',
  ],
  [
    'Run job by name',
    'curl -X POST -H "Authorization: Bearer $PC_TOKEN" -H "X-Project-Id: $PC_PROJECT_ID" "$PC_HOST/api/v1/build-summary"',
  ],
] as const

export function ApiTokensPage() {
  const { data: tokens } = useSuspenseQuery(apiTokensQueryOptions)
  const queryClient = useQueryClient()
  const eventBus = useAppEventBus()
  const [newName, setNewName] = useState('')
  const [lifetimeDays, setLifetimeDays] = useState(90)
  const [created, setCreated] = useState<CreatedApiToken | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const createMutation = useMutation({
    mutationFn: async ({ name, days }: { name: string; days: number }) =>
      createApiToken(name, days, AbortSignal.timeout(30_000)),
  })
  const revokeMutation = useMutation({
    mutationFn: async (tokenId: string) => revokeApiToken(tokenId, AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    const unsubscribeCreate = eventBus.subscribe(
      'settings.api-token-create-requested',
      async ({ name, lifetimeDays: days }) => {
        setMessage(null)
        if (name.trim() === '') {
          setMessage('Give the token a name.')
          return
        }
        try {
          const value = await createMutation.mutateAsync({ name: name.trim(), days })
          setCreated(value)
          setNewName('')
          await queryClient.invalidateQueries({ queryKey: apiTokensQueryOptions.queryKey })
          await eventBus.publish('settings.api-token-created', { tokenId: value.id })
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'The token could not be created.')
        }
      },
    )
    const unsubscribeRevoke = eventBus.subscribe(
      'settings.api-token-revoke-requested',
      async ({ tokenId }) => {
        setMessage(null)
        try {
          await revokeMutation.mutateAsync(tokenId)
          setMessage('Token revoked.')
          await queryClient.invalidateQueries({ queryKey: apiTokensQueryOptions.queryKey })
          await eventBus.publish('settings.api-token-revoked', { tokenId })
        } catch (error: unknown) {
          setMessage(error instanceof Error ? error.message : 'The token could not be revoked.')
        }
      },
    )
    return () => {
      unsubscribeCreate()
      unsubscribeRevoke()
    }
  }, [createMutation, eventBus, queryClient, revokeMutation])

  async function copyExample(command: string): Promise<void> {
    await navigator.clipboard.writeText(command)
    setMessage('Copied example command.')
  }

  const busy = createMutation.isPending || revokeMutation.isPending

  return (
    <div className="settings-page api-tokens-page">
      <title>placecontext — API tokens</title>
      <h1>API tokens</h1>
      <p className="settings-intro">
        Personal tokens authenticate you against the project data and search APIs. The raw secret is
        shown once when created.
      </p>
      <section className="dccard api-docs-card">
        <h2>Usable endpoints</h2>
        <ul>
          {ENDPOINTS.map(([method, path, description]) => (
            <li key={`${method}-${path}`}>
              <code>
                {method} {path}
              </code>{' '}
              — {description}
            </li>
          ))}
        </ul>
        <h3>Example calls</h3>
        {EXAMPLES.map(([title, command]) => (
          <div className="api-example" key={title}>
            <strong>{title}</strong>
            <div>
              <code>{command}</code>
              <button
                className="dcbtn"
                onClick={() => {
                  void copyExample(command)
                }}
                type="button"
              >
                Copy
              </button>
            </div>
          </div>
        ))}
        <p className="settings-hint">
          Include <code>Authorization: Bearer &lt;token&gt;</code> or{' '}
          <code>X-Api-Key: &lt;token&gt;</code>, and one of <code>X-Project-Id</code> /{' '}
          <code>X-Project</code>. Job-run endpoints also require API invocation enabled.
        </p>
      </section>
      {message === null ? null : (
        <div className="settings-message" role="status">
          {message}
        </div>
      )}
      {created === null ? null : (
        <section className="dccard created-token">
          <strong>Copy your token now — it will not be shown again</strong>
          <code>{created.rawToken}</code>
          <button
            className="dcbtn"
            onClick={() => {
              setCreated(null)
            }}
            type="button"
          >
            I&apos;ve copied it
          </button>
        </section>
      )}
      <section className="dccard token-create">
        <h2>Create token</h2>
        <div className="token-create-row">
          <input
            aria-label="Token name"
            onChange={(event) => {
              setNewName(event.target.value)
            }}
            placeholder="CI, local script…"
            value={newName}
          />
          <select
            aria-label="Token lifetime"
            onChange={(event) => {
              setLifetimeDays(Number(event.target.value))
            }}
            value={lifetimeDays}
          >
            <option value="30">30 days</option>
            <option value="90">90 days (default)</option>
            <option value="180">180 days</option>
            <option value="365">1 year (max)</option>
          </select>
          <button
            className="dcbtn primary"
            disabled={busy}
            onClick={() =>
              void eventBus.publish('settings.api-token-create-requested', {
                name: newName,
                lifetimeDays,
              })
            }
            type="button"
          >
            {createMutation.isPending ? 'Creating…' : 'Create'}
          </button>
        </div>
      </section>
      {tokens.length === 0 ? (
        <div className="dccard token-empty">No active tokens.</div>
      ) : (
        <div className="token-list">
          {tokens.map((token) => (
            <article className="dccard token-row" key={token.id}>
              <strong>{token.name}</strong>
              <code>{token.tokenPrefix}…</code>
              <button
                className="dcbtn"
                disabled={busy}
                onClick={() =>
                  void eventBus.publish('settings.api-token-revoke-requested', {
                    tokenId: token.id,
                  })
                }
                type="button"
              >
                Revoke
              </button>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
