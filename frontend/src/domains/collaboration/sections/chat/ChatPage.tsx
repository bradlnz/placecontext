import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { type SyntheticEvent, useState } from 'react'

import { workspaceProjectsQuery } from '../../../workspace/api/workspace-query-options'
import { sendChatMessage, updateChatSettings } from '../../api/chat-api'
import { chatPageQueryOptions, chatQueryKeys } from '../../api/chat-query'
import type {
  ChatConfig,
  ChatPageModel,
  ChatSession,
  UpdateChatSettingsRequest,
} from '../../model/chat'

const STARTER_PROMPTS = [
  'Give me an overview of this project',
  'Show me the recent job runs and their status',
  'What are the hotspots in the project dependency graph?',
  'List the recent artifacts produced by job runs',
  'List the project data tables',
] as const

type SelectedSession = string | null | undefined

export function ChatPage() {
  const projectsQuery = useSuspenseQuery(workspaceProjectsQuery)
  const project = projectsQuery.data[0]

  if (project === undefined) {
    return (
      <section className="react-chat-empty-workspace">
        <h2>Agent Chat</h2>
        <p>Add a project before starting a conversation.</p>
      </section>
    )
  }

  return <ProjectChat key={project.id} projectId={project.id} projectName={project.name} />
}

function ProjectChat({ projectId, projectName }: { projectId: string; projectName: string }) {
  const queryClient = useQueryClient()
  const pageQuery = useSuspenseQuery(chatPageQueryOptions(projectId))
  const [selectedSessionId, setSelectedSessionId] = useState<SelectedSession>(undefined)
  const [input, setInput] = useState('')
  const [pendingMessage, setPendingMessage] = useState<string | null>(null)
  const [showPanel, setShowPanel] = useState(true)
  const [settings, setSettings] = useState<UpdateChatSettingsRequest | null>(null)

  const activeSession = findActiveSession(pageQuery.data.sessions, selectedSessionId)
  const sendMutation = useMutation({
    mutationFn: async ({ message, sessionId }: { message: string; sessionId: string | null }) => {
      const controller = new AbortController()
      return sendChatMessage(projectId, sessionId, message, controller.signal)
    },
    onSuccess: (session) => {
      setSelectedSessionId(session.id)
      queryClient.setQueryData<ChatPageModel>(chatQueryKeys.page(projectId), (current) =>
        current === undefined
          ? current
          : { ...current, sessions: upsertSession(current.sessions, session) },
      )
    },
    onError: (_error, variables) => {
      setInput(variables.message)
    },
    onSettled: () => {
      setPendingMessage(null)
    },
  })
  const settingsMutation = useMutation({
    mutationFn: async (request: UpdateChatSettingsRequest) => {
      const controller = new AbortController()
      return updateChatSettings(projectId, request, controller.signal)
    },
    onSuccess: (config) => {
      queryClient.setQueryData<ChatPageModel>(chatQueryKeys.page(projectId), (current) =>
        current === undefined ? current : { ...current, config },
      )
      setSettings(null)
    },
  })

  function submit(event: SyntheticEvent<HTMLFormElement>): void {
    event.preventDefault()
    send(input)
  }

  function send(message: string): void {
    const trimmed = message.trim()
    if (trimmed === '' || sendMutation.isPending) return
    setPendingMessage(trimmed)
    setInput('')
    sendMutation.mutate({
      message: trimmed,
      sessionId: activeSession?.id ?? null,
    })
  }

  function openSettings(): void {
    setSettings(settingsFromConfig(pageQuery.data.config))
  }

  return (
    <section className="react-chat-page">
      <header className="react-chat-head">
        <div>
          <h2>Agent Chat</h2>
          <p>
            {pageQuery.data.config.enabled ? 'Chat backend active' : 'Agent disabled'} —{' '}
            {projectName}
          </p>
        </div>
        <div className="react-chat-controls">
          <button
            className="dcbtn sm"
            onClick={() => {
              setShowPanel((current) => !current)
            }}
            type="button"
          >
            {showPanel ? 'Hide Panel' : 'Show Panel'}
          </button>
          <button className="dcbtn sm" onClick={openSettings} type="button">
            ⚙ Settings
          </button>
        </div>
      </header>

      <div className="react-chat-layout">
        <div className="react-chat-main">
          <div className="react-chat-messages" aria-live="polite">
            {activeSession === undefined && pendingMessage === null ? (
              <ChatWelcome onPrompt={send} disabled={sendMutation.isPending} />
            ) : (
              <>
                {activeSession?.messages.map((message, index) => (
                  <article
                    className={`react-chat-message ${message.role}`}
                    key={`${message.timestamp}-${String(index)}`}
                  >
                    <div className="react-chat-avatar" aria-hidden="true">
                      {message.role === 'user' ? 'You' : 'PC'}
                    </div>
                    <div className="react-chat-message-body">
                      <div className="react-chat-message-meta">
                        <strong>{message.role === 'user' ? 'You' : 'Assistant'}</strong>
                        {message.role === 'assistant' ? (
                          <button
                            aria-label="Copy response"
                            className="react-chat-copy"
                            onClick={() => void copyText(message.content)}
                            type="button"
                          >
                            Copy
                          </button>
                        ) : null}
                      </div>
                      <div className="react-chat-content">{message.content}</div>
                      {message.role === 'assistant' ? (
                        <div className="react-chat-quick-actions">
                          <button
                            onClick={() => {
                              send(`Summarize this response:\n\n${message.content}`)
                            }}
                            type="button"
                          >
                            Summarize
                          </button>
                          <button
                            onClick={() => {
                              send(`Explain this in more detail:\n\n${message.content}`)
                            }}
                            type="button"
                          >
                            Explain
                          </button>
                          <button
                            onClick={() => {
                              send(
                                `Turn this into an implementation example:\n\n${message.content}`,
                              )
                            }}
                            type="button"
                          >
                            Code
                          </button>
                          <button
                            onClick={() => {
                              send(`Visualize this as a graph:\n\n${message.content}`)
                            }}
                            type="button"
                          >
                            Graph
                          </button>
                        </div>
                      ) : null}
                    </div>
                  </article>
                ))}
                {pendingMessage !== null ? (
                  <>
                    <article className="react-chat-message user pending">
                      <div className="react-chat-avatar" aria-hidden="true">
                        You
                      </div>
                      <div className="react-chat-message-body">
                        <strong>You</strong>
                        <div className="react-chat-content">{pendingMessage}</div>
                      </div>
                    </article>
                    <article className="react-chat-message assistant pending">
                      <div className="react-chat-avatar" aria-hidden="true">
                        PC
                      </div>
                      <div className="react-chat-message-body">
                        <strong>Assistant</strong>
                        <div className="react-chat-thinking">
                          <span /> Thinking…
                        </div>
                      </div>
                    </article>
                  </>
                ) : null}
              </>
            )}
          </div>

          {sendMutation.error instanceof Error ? (
            <p className="react-chat-error" role="alert">
              {sendMutation.error.message}
            </p>
          ) : null}
          <form className="react-chat-composer" onSubmit={submit}>
            <textarea
              aria-label="Message"
              disabled={sendMutation.isPending}
              onChange={(event) => {
                setInput(event.target.value)
              }}
              placeholder="Ask about your project…"
              rows={2}
              value={input}
            />
            <button
              className="dcbtn primary"
              disabled={input.trim() === '' || sendMutation.isPending}
              type="submit"
            >
              Send
            </button>
          </form>
        </div>

        {showPanel ? (
          <ChatPanel
            activeSessionId={activeSession?.id}
            config={pageQuery.data.config}
            onNew={() => {
              setSelectedSessionId(null)
            }}
            onSelect={setSelectedSessionId}
            sessions={pageQuery.data.sessions}
          />
        ) : null}
      </div>

      {settings !== null ? (
        <ChatSettingsDialog
          error={settingsMutation.error}
          onCancel={() => {
            setSettings(null)
          }}
          onChange={setSettings}
          onSave={() => {
            settingsMutation.mutate(settings)
          }}
          pending={settingsMutation.isPending}
          settings={settings}
        />
      ) : null}
    </section>
  )
}

function ChatWelcome({
  onPrompt,
  disabled,
}: {
  onPrompt: (prompt: string) => void
  disabled: boolean
}) {
  return (
    <div className="react-chat-welcome">
      <div className="react-chat-welcome-mark" aria-hidden="true">
        PC
      </div>
      <h3>What can I help you explore?</h3>
      <p>Ask me about your project data, job runs, or request a graph.</p>
      <div className="react-chat-starters">
        {STARTER_PROMPTS.map((prompt) => (
          <button
            disabled={disabled}
            key={prompt}
            onClick={() => {
              onPrompt(prompt)
            }}
            type="button"
          >
            {prompt}
          </button>
        ))}
      </div>
      <small>Or type your own question below.</small>
    </div>
  )
}

function ChatPanel({
  activeSessionId,
  config,
  onNew,
  onSelect,
  sessions,
}: {
  activeSessionId: string | undefined
  config: ChatConfig
  onNew: () => void
  onSelect: (sessionId: string) => void
  sessions: ChatSession[]
}) {
  return (
    <aside className="react-chat-panel" aria-label="Chat sessions">
      <div className="react-chat-panel-head">
        <strong>Conversations</strong>
        <button className="dcbtn xs primary" onClick={onNew} type="button">
          + New
        </button>
      </div>
      <div className="react-chat-session-list">
        {sessions.length === 0 ? <p>No saved conversations yet.</p> : null}
        {sessions.map((session) => (
          <button
            aria-current={session.id === activeSessionId ? 'true' : undefined}
            className={session.id === activeSessionId ? 'active' : undefined}
            key={session.id}
            onClick={() => {
              onSelect(session.id)
            }}
            type="button"
          >
            <strong>{session.title ?? 'New conversation'}</strong>
            <span>{formatRelativeDate(session.updatedAt)}</span>
          </button>
        ))}
      </div>
      <div className="react-chat-agent-card">
        <span className={config.enabled ? 'online' : 'offline'} />
        <div>
          <strong>{config.enabled ? 'Agent ready' : 'Agent disabled'}</strong>
          <small>{config.baseModel}</small>
        </div>
      </div>
    </aside>
  )
}

function ChatSettingsDialog({
  error,
  onCancel,
  onChange,
  onSave,
  pending,
  settings,
}: {
  error: Error | null
  onCancel: () => void
  onChange: (settings: UpdateChatSettingsRequest) => void
  onSave: () => void
  pending: boolean
  settings: UpdateChatSettingsRequest
}) {
  return (
    <div className="modal-backdrop">
      <div
        aria-labelledby="chat-settings-title"
        aria-modal="true"
        className="modal react-chat-settings"
        role="dialog"
      >
        <div className="modal-head">
          <div>
            <h3 id="chat-settings-title">Agent settings</h3>
            <p>Control the project prompt and retrieval behaviour.</p>
          </div>
          <button aria-label="Close settings" className="icon-btn" onClick={onCancel} type="button">
            ×
          </button>
        </div>
        <div className="modal-body">
          <label>
            Base model
            <input
              onChange={(event) => {
                onChange({ ...settings, baseModel: event.target.value })
              }}
              value={settings.baseModel}
            />
          </label>
          <label>
            System prompt
            <textarea
              onChange={(event) => {
                onChange({ ...settings, systemPrompt: event.target.value })
              }}
              rows={8}
              value={settings.systemPrompt}
            />
          </label>
          <div className="react-chat-settings-grid">
            <label>
              Temperature
              <input
                max="2"
                min="0"
                onChange={(event) => {
                  onChange({ ...settings, temperature: Number(event.target.value) })
                }}
                step="0.1"
                type="number"
                value={settings.temperature}
              />
            </label>
            <label>
              Context chunks
              <input
                min="1"
                onChange={(event) => {
                  onChange({ ...settings, maxContextChunks: Number(event.target.value) })
                }}
                type="number"
                value={settings.maxContextChunks}
              />
            </label>
          </div>
          <label className="react-chat-checkbox">
            <input
              checked={settings.enabled}
              onChange={(event) => {
                onChange({ ...settings, enabled: event.target.checked })
              }}
              type="checkbox"
            />
            Agent enabled for this project
          </label>
          {error instanceof Error ? (
            <p className="react-chat-error" role="alert">
              {error.message}
            </p>
          ) : null}
        </div>
        <div className="modal-actions">
          <button className="dcbtn" onClick={onCancel} type="button">
            Cancel
          </button>
          <button className="dcbtn primary" disabled={pending} onClick={onSave} type="button">
            {pending ? 'Saving…' : 'Save settings'}
          </button>
        </div>
      </div>
    </div>
  )
}

function findActiveSession(
  sessions: ChatSession[],
  selected: SelectedSession,
): ChatSession | undefined {
  if (selected === null) return undefined
  return sessions.find((session) => session.id === selected) ?? sessions[0]
}

function upsertSession(sessions: ChatSession[], next: ChatSession): ChatSession[] {
  return [next, ...sessions.filter((session) => session.id !== next.id)]
}

function settingsFromConfig(config: ChatConfig): UpdateChatSettingsRequest {
  return {
    baseModel: config.baseModel,
    systemPrompt: config.systemPrompt,
    preamble: config.preamble,
    toolCatalog: config.toolCatalog,
    launchpadToolCatalog: config.launchpadToolCatalog,
    maxContextChunks: config.maxContextChunks,
    temperature: config.temperature,
    topP: config.topP,
    enabled: config.enabled,
  }
}

function formatRelativeDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  )
}

async function copyText(value: string): Promise<void> {
  await navigator.clipboard.writeText(value)
}
