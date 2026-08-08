import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'

import { createProjectSecret, deleteProjectSecret } from '../../api/project-secrets-api'
import { projectSecretsQueryOptions } from '../../api/project-secrets-query'

type VaultCommand =
  { kind: 'create'; name: string; value: string } | { kind: 'delete'; name: string }

export function VaultPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const queryOptions = projectSecretsQueryOptions(projectId)
  const { data: secrets } = useSuspenseQuery(queryOptions)
  const queryClient = useQueryClient()
  const [newName, setNewName] = useState('')
  const [newValue, setNewValue] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [addError, setAddError] = useState<string | null>(null)
  const mutation = useMutation({
    mutationFn: async (command: VaultCommand) => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'create')
        return createProjectSecret(projectId, { name: command.name, value: command.value }, signal)
      await deleteProjectSecret(projectId, command.name, signal)
      return null
    },
    onSuccess: async (_result, command) => {
      await queryClient.invalidateQueries({ queryKey: queryOptions.queryKey })
      if (command.kind === 'create') {
        setNewName('')
        setNewValue('')
        setMessage(`Secret '${command.name}' saved.`)
      } else setMessage(`Secret '${command.name}' deleted.`)
    },
  })

  async function addSecret(): Promise<void> {
    setAddError(null)
    const name = newName.trim()
    if (name === '') {
      setAddError('Name is required.')
      return
    }
    if (newValue === '') {
      setAddError('Value is required.')
      return
    }
    setMessage(null)
    try {
      await mutation.mutateAsync({ kind: 'create', name, value: newValue })
    } catch (error: unknown) {
      setAddError(error instanceof Error ? error.message : 'The secret could not be saved.')
    }
  }

  async function removeSecret(name: string): Promise<void> {
    setMessage(null)
    try {
      await mutation.mutateAsync({ kind: 'delete', name })
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'The secret could not be deleted.')
    }
  }

  return (
    <div className="vault-page">
      <title>PlaceContext — Vault</title>
      <header className="vault-head">
        <h1>Vault</h1>
      </header>
      <p className="vault-intro">
        Encrypted secrets injected as environment variables into this project's jobs at run time.
        Values are encrypted at rest and never displayed after creation.
      </p>
      {message === null ? null : (
        <div className="vault-message" role="status">
          {message}
        </div>
      )}
      <section className="dccard vault-add-card">
        <h2>Add secret</h2>
        <div className="vault-add-row">
          <input
            aria-label="Secret name"
            className="dcinput"
            onChange={(event) => {
              setNewName(event.target.value)
            }}
            placeholder="API_KEY"
            value={newName}
          />
          <input
            aria-label="Secret value"
            className="dcinput"
            onChange={(event) => {
              setNewValue(event.target.value)
            }}
            placeholder="••••••••"
            type="password"
            value={newValue}
          />
          <button
            className="dcbtn primary"
            disabled={mutation.isPending}
            onClick={() => void addSecret()}
            type="button"
          >
            {mutation.isPending && mutation.variables.kind === 'create' ? 'Saving…' : 'Add'}
          </button>
        </div>
        {addError === null ? null : (
          <div className="vault-add-error" role="alert">
            {addError}
          </div>
        )}
      </section>
      {secrets.length === 0 ? (
        <div className="dccard vault-empty">No secrets yet.</div>
      ) : (
        <div className="vault-list">
          {secrets.map((secret) => (
            <article className="dccard vault-secret-row" key={secret.name}>
              <strong>{secret.name}</strong>
              <span>••••••••</span>
              <small>added {secret.createdAtDisplay}</small>
              <button
                className="dcbtn"
                disabled={mutation.isPending}
                onClick={() => void removeSecret(secret.name)}
                type="button"
              >
                Delete
              </button>
            </article>
          ))}
        </div>
      )}
    </div>
  )
}
