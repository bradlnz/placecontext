import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { defineEventType, emitEvent } from '../../api/events-api'
import { eventsQueryOptions } from '../../api/events-query'

export function EventsPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const options = eventsQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [definitionOpen, setDefinitionOpen] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [schema, setSchema] = useState('')
  const [emitName, setEmitName] = useState<string | null>(null)
  const [payload, setPayload] = useState('')
  const [message, setMessage] = useState<{ text: string; error: boolean } | null>(null)
  const mutation = useMutation({
    mutationFn: async (command: { kind: 'define' } | { kind: 'emit'; name: string }) => {
      const signal = AbortSignal.timeout(30_000)
      return command.kind === 'define'
        ? defineEventType(projectId, { name, description, payloadSchema: schema }, signal)
        : emitEvent(projectId, command.name, payload, signal)
    },
  })
  const custom = data.types.filter((type) => !type.isBuiltIn).length
  const active = data.triggers.filter((trigger) => trigger.enabled).length
  const subscriberCount = (eventName: string) =>
    data.triggers.filter(
      (trigger) =>
        trigger.enabled && trigger.eventName?.toLocaleLowerCase() === eventName.toLocaleLowerCase(),
    ).length
  const subscribed = data.types.filter((type) => subscriberCount(type.name) > 0).length

  async function define(): Promise<void> {
    if (name.trim() === '') {
      setMessage({ text: 'Name is required.', error: true })
      return
    }
    try {
      await mutation.mutateAsync({ kind: 'define' })
      await client.invalidateQueries({ queryKey: options.queryKey })
      setDefinitionOpen(false)
      setMessage({ text: `Created event type '${name.trim()}'.`, error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The event type could not be created.',
        error: true,
      })
    }
  }

  async function emit(): Promise<void> {
    if (emitName === null) return
    try {
      const result = await mutation.mutateAsync({ kind: 'emit', name: emitName })
      await client.invalidateQueries({ queryKey: options.queryKey })
      setMessage({
        text:
          'triggeredRuns' in result
            ? `Emitted '${emitName}' — ${String(result.triggeredRuns)} trigger(s) fired.`
            : `Emitted '${emitName}'.`,
        error: false,
      })
      setEmitName(null)
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The event could not be emitted.',
        error: true,
      })
    }
  }

  return (
    <div className="events-page-react">
      <title>PlaceContext — Events</title>
      <header>
        <div>
          <h1>Events</h1>
          <p>See workspace activity and manage event types that can start Jobs automatically.</p>
        </div>
        <div>
          <button
            className="dcbtn"
            onClick={() => void navigate(`/project/${projectId}/jobs`)}
            type="button"
          >
            Manage triggers
          </button>
          <button
            className="dcbtn primary"
            onClick={() => {
              setName('')
              setDescription('')
              setSchema('')
              setDefinitionOpen(true)
            }}
            type="button"
          >
            ＋ New event type
          </button>
        </div>
      </header>
      {message === null ? null : (
        <div
          className={message.error ? 'events-message-react error' : 'events-message-react'}
          role={message.error ? 'alert' : 'status'}
        >
          {message.text}
        </div>
      )}
      <section className="events-summary-react" aria-label="Event summary">
        <div>
          <strong>{data.types.length}</strong>
          <span>event types</span>
        </div>
        <div>
          <strong>{custom}</strong>
          <span>custom</span>
        </div>
        <div>
          <strong>{active}</strong>
          <span>active triggers</span>
        </div>
        <div>
          <strong>{data.log.length}</strong>
          <span>recent events</span>
        </div>
        <i>
          <span
            style={{
              width: `${String(data.types.length === 0 ? 0 : Math.round((subscribed * 100) / data.types.length))}%`,
            }}
          />
        </i>
      </section>
      <div className="events-layout-react">
        <section className="dccard events-panel-react">
          <header>
            <div>
              <strong>Event types</strong>
              <span>Choose one to emit manually</span>
            </div>
            <span>{data.types.length} available</span>
          </header>
          {data.types.length === 0 ? (
            <p>No event types yet.</p>
          ) : (
            data.types.map((type) => (
              <article key={type.name}>
                <span>{type.isBuiltIn ? '◆' : '◇'}</span>
                <div>
                  <div>
                    <strong>{type.name}</strong>
                    <small>{type.isBuiltIn ? 'built-in' : 'custom'}</small>
                    {subscriberCount(type.name) === 0 ? null : (
                      <small>{subscriberCount(type.name)} active triggers</small>
                    )}
                  </div>
                  <p>{type.description ?? 'No description provided.'}</p>
                  {type.payloadSchema === null ? null : (
                    <details>
                      <summary>Payload guidance</summary>
                      <pre>{type.payloadSchema}</pre>
                    </details>
                  )}
                </div>
                <button
                  className="dcbtn"
                  onClick={() => {
                    setEmitName(type.name)
                    setPayload('')
                  }}
                  type="button"
                >
                  Emit
                </button>
              </article>
            ))
          )}
        </section>
        <section className="dccard events-panel-react">
          <header>
            <div>
              <strong>Recent activity</strong>
              <span>Newest first</span>
            </div>
            <span>latest 50</span>
          </header>
          {data.log.length === 0 ? (
            <p>No events emitted yet.</p>
          ) : (
            data.log.map((item) => (
              <article className="event-activity-react" key={item.id}>
                <span className={item.source.toLocaleLowerCase()} />
                <div>
                  <div>
                    <strong>{item.name}</strong>
                    <small>{item.sourceLabel}</small>
                  </div>
                  <time dateTime={item.occurredAt}>{item.occurredAtDisplay}</time>
                  {item.payload === null ? (
                    <small>No payload</small>
                  ) : (
                    <details>
                      <summary>View payload</summary>
                      <pre>{item.payload}</pre>
                    </details>
                  )}
                </div>
              </article>
            ))
          )}
        </section>
      </div>
      {definitionOpen ? (
        <div className="event-modal-react">
          <section className="dccard" role="dialog" aria-label="Define an event">
            <header>
              <div>
                <strong>Define an event</strong>
                <span>Name an activity that Jobs can listen for.</span>
              </div>
              <button
                aria-label="Close"
                onClick={() => {
                  setDefinitionOpen(false)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <label>
              Name
              <input
                className="dcinput"
                onChange={(event) => {
                  setName(event.target.value)
                }}
                value={name}
              />
            </label>
            <label>
              Description
              <input
                className="dcinput"
                onChange={(event) => {
                  setDescription(event.target.value)
                }}
                value={description}
              />
            </label>
            <label>
              Payload guidance
              <textarea
                className="dcinput"
                onChange={(event) => {
                  setSchema(event.target.value)
                }}
                rows={7}
                value={schema}
              />
            </label>
            <footer>
              <button
                className="dcbtn"
                onClick={() => {
                  setDefinitionOpen(false)
                }}
                type="button"
              >
                Cancel
              </button>
              <button
                className="dcbtn primary"
                disabled={mutation.isPending}
                onClick={() => void define()}
                type="button"
              >
                Create event type
              </button>
            </footer>
          </section>
        </div>
      ) : null}
      {emitName === null ? null : (
        <div className="event-modal-react">
          <section className="dccard" role="dialog" aria-label={`Emit ${emitName}`}>
            <header>
              <div>
                <strong>{emitName}</strong>
                <span>{subscriberCount(emitName)} active trigger(s)</span>
              </div>
              <button
                aria-label="Close"
                onClick={() => {
                  setEmitName(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <label>
              Payload · optional JSON
              <textarea
                className="dcinput"
                onChange={(event) => {
                  setPayload(event.target.value)
                }}
                rows={9}
                value={payload}
              />
            </label>
            <footer>
              <button
                className="dcbtn"
                onClick={() => {
                  setEmitName(null)
                }}
                type="button"
              >
                Cancel
              </button>
              <button
                className="dcbtn primary"
                disabled={mutation.isPending}
                onClick={() => void emit()}
                type="button"
              >
                Emit event
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  )
}
