import { useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { type SyntheticEvent, useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'

import {
  configureCrmPortal,
  deleteCrmAppointment,
  deleteCrmAutomation,
  deleteCrmCalendar,
  deleteCrmClient,
  disableCrmIngestion,
  fetchCrmIngestionSettings,
  moveCrmClient,
  removeCrmArtifact,
  rotateCrmIngestionToken,
  runCrmAutomation,
  saveCrmAppointment,
  saveCrmAutomation,
  saveCrmCalendar,
  saveCrmClient,
  saveCrmIngestionOrigin,
  sendCrmCommunication,
  setCrmAutomationEnabled,
  setCrmChainAssignments,
} from '../../api/crm-api'
import { crmClientQueryOptions, crmPageQueryOptions, crmQueryKeys } from '../../api/crm-query'
import type { CrmAutomation, CrmClient, CrmPageModel } from '../../model/crm'

type Section =
  | 'conversations'
  | 'calendars'
  | 'contacts'
  | 'opportunities'
  | 'automations'
  | 'portals'
  | 'settings'
const sections: { id: Section; label: string }[] = [
  { id: 'conversations', label: 'Conversations' },
  { id: 'calendars', label: 'Calendars' },
  { id: 'contacts', label: 'Contacts' },
  { id: 'opportunities', label: 'Opportunities' },
  { id: 'automations', label: 'Automations' },
  { id: 'portals', label: 'Customer portals' },
  { id: 'settings', label: 'Settings' },
]
const stages = ['Lead', 'Qualified', 'Proposal', 'Customer', 'Churned']

export function CrmPage() {
  const { projectId = '' } = useParams()
  const { data } = useSuspenseQuery(crmPageQueryOptions(projectId))
  const queryClient = useQueryClient()
  const [section, setSection] = useState<Section>('opportunities')
  const [search, setSearch] = useState('')
  const [stage, setStage] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [dialog, setDialog] = useState<
    'client' | 'appointment' | 'calendar' | 'automation' | 'portal' | null
  >(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const selected = data.clients.find((client) => client.id === selectedId)

  const filteredClients = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return data.clients
      .filter((client) => {
        const matchesStage =
          stage === null || client.lifecycleStage.toLowerCase() === stage.toLowerCase()
        const haystack = [client.name, client.company, client.email, client.phone]
          .filter(Boolean)
          .join(' ')
          .toLowerCase()
        return matchesStage && (needle === '' || haystack.includes(needle))
      })
      .toSorted((left, right) => left.name.localeCompare(right.name))
  }, [data.clients, search, stage])

  async function command(
    action: (signal: AbortSignal) => Promise<unknown>,
    success: string,
  ): Promise<void> {
    setPending(true)
    setError(null)
    setMessage(null)
    try {
      await action(AbortSignal.timeout(30_000))
      await queryClient.invalidateQueries({ queryKey: crmQueryKeys.page(projectId) })
      if (selectedId !== null)
        await queryClient.invalidateQueries({
          queryKey: crmQueryKeys.client(projectId, selectedId),
        })
      setDialog(null)
      setMessage(success)
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : 'The CRM request failed.')
    } finally {
      setPending(false)
    }
  }

  function openCreate(next: typeof dialog): void {
    setEditingId(null)
    setDialog(next)
    setError(null)
  }
  function primaryAction(): void {
    if (section === 'calendars') openCreate('appointment')
    else if (section === 'automations') openCreate('automation')
    else if (section === 'portals') openCreate('portal')
    else openCreate('client')
  }

  return (
    <section className="crm-page-react">
      <title>PlaceContext — CRM</title>
      <header className="crm-head-react">
        <div>
          <span>Customer operations</span>
          <h1>{sectionTitle(section)}</h1>
          <p>{sectionDescription(section)}</p>
        </div>
        {section === 'settings' || section === 'conversations' ? null : (
          <button className="dcbtn primary" onClick={primaryAction} type="button">
            ＋{' '}
            {section === 'calendars'
              ? 'New appointment'
              : section === 'automations'
                ? 'New automation'
                : section === 'portals'
                  ? 'Configure portal'
                  : 'Add client'}
          </button>
        )}
      </header>
      <div className="crm-shell-react">
        <nav aria-label="CRM sections">
          {sections.map((item) => (
            <button
              aria-current={section === item.id ? 'page' : undefined}
              className={section === item.id ? 'active' : undefined}
              key={item.id}
              onClick={() => {
                setSection(item.id)
              }}
              type="button"
            >
              <span>{sectionIcon(item.id)}</span>
              {item.label}
              <small>{sectionCount(item.id, data)}</small>
            </button>
          ))}
        </nav>
        <main>
          {message === null ? null : (
            <div className="crm-message-react" role="status">
              {message}
            </div>
          )}
          {error === null ? null : (
            <div className="crm-message-react error" role="alert">
              {error}
            </div>
          )}
          {section === 'contacts' || section === 'opportunities' || section === 'portals' ? (
            <ClientWorkspace
              clients={filteredClients}
              data={data}
              onEdit={(client) => {
                setEditingId(client.id)
                setDialog(section === 'portals' ? 'portal' : 'client')
              }}
              onOpen={(client) => {
                setSelectedId(client.id)
              }}
              onSearch={setSearch}
              onStage={setStage}
              search={search}
              section={section}
              selectedStage={stage}
            />
          ) : null}
          {section === 'conversations' ? (
            <Conversations clients={data.clients} onOpen={setSelectedId} selected={selected} />
          ) : null}
          {section === 'calendars' ? (
            <Calendars
              data={data}
              onDeleteAppointment={(id) =>
                void command((signal) => deleteCrmAppointment(id, signal), 'Appointment deleted.')
              }
              onDeleteCalendar={(id) =>
                void command((signal) => deleteCrmCalendar(id, signal), 'Calendar deleted.')
              }
              onNewCalendar={() => {
                openCreate('calendar')
              }}
            />
          ) : null}
          {section === 'automations' ? (
            <Automations
              automations={data.automations}
              onDelete={(item) =>
                void command(
                  (signal) => deleteCrmAutomation(item.id, signal),
                  `${item.name} deleted.`,
                )
              }
              onEdit={(item) => {
                setEditingId(item.id)
                setDialog('automation')
              }}
              onToggle={(item) =>
                void command(
                  (signal) => setCrmAutomationEnabled(item.id, !item.enabled, signal),
                  `${item.name} ${item.enabled ? 'paused' : 'enabled'}.`,
                )
              }
            />
          ) : null}
          {section === 'settings' ? <CrmSettings projectId={projectId} /> : null}
        </main>
      </div>
      {selected === undefined ? null : (
        <ClientDrawer
          client={selected}
          data={data}
          onClose={() => {
            setSelectedId(null)
          }}
          onCommand={command}
          projectId={projectId}
        />
      )}
      {dialog === null ? null : (
        <CrmDialog
          data={data}
          editingId={editingId}
          kind={dialog}
          onClose={() => {
            setDialog(null)
          }}
          onSubmit={command}
          pending={pending}
          projectId={projectId}
        />
      )}
    </section>
  )
}

function ClientWorkspace({
  clients,
  data,
  onEdit,
  onOpen,
  onSearch,
  onStage,
  search,
  section,
  selectedStage,
}: {
  clients: CrmClient[]
  data: CrmPageModel
  onEdit: (client: CrmClient) => void
  onOpen: (client: CrmClient) => void
  onSearch: (value: string) => void
  onStage: (value: string | null) => void
  search: string
  section: Section
  selectedStage: string | null
}) {
  return (
    <>
      <div className="crm-toolbar-react">
        <input
          aria-label="Search clients"
          onChange={(event) => {
            onSearch(event.target.value)
          }}
          placeholder="Search name, company, email, or phone…"
          value={search}
        />
      </div>
      {section === 'opportunities' ? (
        <section className="dccard crm-lifecycle-react">
          <header>
            <div>
              <strong>Lifecycle overview</strong>
              <small>Select a stage to filter the client directory</small>
            </div>
            <span>
              <b>{data.clients.length}</b> total
            </span>
          </header>
          <div>
            {stages.map((item) => (
              <button
                className={
                  selectedStage === item ? `active ${item.toLowerCase()}` : item.toLowerCase()
                }
                key={item}
                onClick={() => {
                  onStage(selectedStage === item ? null : item)
                }}
                type="button"
              >
                <i />
                <span>
                  <strong>{item}</strong>
                  <small>{stageDescription(item)}</small>
                </span>
                <b>
                  {
                    data.clients.filter(
                      (client) => client.lifecycleStage.toLowerCase() === item.toLowerCase(),
                    ).length
                  }
                </b>
              </button>
            ))}
          </div>
        </section>
      ) : null}
      <section className="crm-directory-react">
        <header>
          <div>
            <strong>{section === 'portals' ? 'Portal customers' : 'Client directory'}</strong>
            <small>{clients.length} results</small>
          </div>
        </header>
        {clients.length === 0 ? (
          <div className="crm-empty-react">No clients match this view.</div>
        ) : (
          <div>
            {clients.map((client) => (
              <article key={client.id}>
                <button
                  className="crm-client-main"
                  onClick={() => {
                    onOpen(client)
                  }}
                  type="button"
                >
                  <span className={client.lifecycleStage.toLowerCase()}>
                    {initials(client.name)}
                  </span>
                  <div>
                    <strong>{client.name}</strong>
                    <small>{client.company ?? 'Individual client'}</small>
                  </div>
                  <div>
                    <span>{client.email ?? 'No email'}</span>
                    <small>{client.phone ?? 'No phone'}</small>
                  </div>
                  <b className={client.lifecycleStage.toLowerCase()}>{client.lifecycleStage}</b>
                </button>
                <button
                  aria-label={`Edit ${client.name}`}
                  onClick={() => {
                    onEdit(client)
                  }}
                  type="button"
                >
                  •••
                </button>
              </article>
            ))}
          </div>
        )}
      </section>
    </>
  )
}

function Conversations({
  clients,
  onOpen,
  selected,
}: {
  clients: CrmClient[]
  onOpen: (id: string) => void
  selected: CrmClient | undefined
}) {
  return (
    <section className="crm-conversations-react">
      <aside>
        <header>
          <strong>Conversations</strong>
          <small>{clients.length} customers</small>
        </header>
        {clients.map((client) => (
          <button
            className={selected?.id === client.id ? 'active' : undefined}
            key={client.id}
            onClick={() => {
              onOpen(client.id)
            }}
            type="button"
          >
            <span>{initials(client.name)}</span>
            <div>
              <strong>{client.name}</strong>
              <small>{client.email ?? client.phone ?? 'No contact details'}</small>
            </div>
          </button>
        ))}
      </aside>
      <div>
        {selected === undefined ? (
          <div className="crm-empty-react">Choose a customer to open their shared timeline.</div>
        ) : (
          <div className="crm-conversation-welcome">
            <span>{initials(selected.name)}</span>
            <h2>{selected.name}</h2>
            <p>
              Open the customer drawer to review messages, internal notes, workflow runs, and
              artifacts.
            </p>
            <button
              className="dcbtn primary"
              onClick={() => {
                onOpen(selected.id)
              }}
              type="button"
            >
              Open conversation
            </button>
          </div>
        )}
      </div>
    </section>
  )
}

function Calendars({
  data,
  onDeleteAppointment,
  onDeleteCalendar,
  onNewCalendar,
}: {
  data: CrmPageModel
  onDeleteAppointment: (id: string) => void
  onDeleteCalendar: (id: string) => void
  onNewCalendar: () => void
}) {
  return (
    <div className="crm-calendar-layout-react">
      <aside className="dccard">
        <header>
          <strong>Calendars</strong>
          <button onClick={onNewCalendar} type="button">
            ＋
          </button>
        </header>
        {data.calendars.map((calendar) => (
          <div key={calendar.id}>
            <i style={{ background: calendar.color }} />
            <span>{calendar.name}</span>
            <button
              aria-label={`Delete ${calendar.name}`}
              onClick={() => {
                onDeleteCalendar(calendar.id)
              }}
              type="button"
            >
              ×
            </button>
          </div>
        ))}
      </aside>
      <section className="dccard crm-appointments-react">
        <header>
          <div>
            <strong>Upcoming appointments</strong>
            <small>Customer meetings across project calendars</small>
          </div>
        </header>
        {data.appointments.length === 0 ? (
          <div className="crm-empty-react">No appointments scheduled.</div>
        ) : (
          data.appointments
            .toSorted((a, b) => a.startsAt.localeCompare(b.startsAt))
            .map((appointment) => (
              <article key={appointment.id}>
                <time>
                  <b>{new Date(appointment.startsAt).getDate()}</b>
                  {new Intl.DateTimeFormat(undefined, { month: 'short' }).format(
                    new Date(appointment.startsAt),
                  )}
                </time>
                <div>
                  <strong>{appointment.title}</strong>
                  <small>
                    {appointment.clientName ?? 'No client'} · {formatDate(appointment.startsAt)}
                    {appointment.location === null ? '' : ` · ${appointment.location}`}
                  </small>
                </div>
                <button
                  aria-label={`Delete ${appointment.title}`}
                  onClick={() => {
                    onDeleteAppointment(appointment.id)
                  }}
                  type="button"
                >
                  ×
                </button>
              </article>
            ))
        )}
      </section>
    </div>
  )
}

function Automations({
  automations,
  onDelete,
  onEdit,
  onToggle,
}: {
  automations: CrmAutomation[]
  onDelete: (item: CrmAutomation) => void
  onEdit: (item: CrmAutomation) => void
  onToggle: (item: CrmAutomation) => void
}) {
  return (
    <section className="crm-automations-react">
      {automations.length === 0 ? (
        <div className="dccard crm-empty-react">No CRM automations configured.</div>
      ) : (
        automations.map((item) => (
          <article className="dccard" key={item.id}>
            <span className={item.enabled ? 'enabled' : undefined}>⌁</span>
            <div>
              <strong>{item.name}</strong>
              <p>
                When {item.eventType}
                {item.lifecycleStage === null ? '' : ` · ${item.lifecycleStage}`} → {item.chainName}
              </p>
              <small>
                {item.chainSteps} chain steps · updated {formatDate(item.updatedAt)}
              </small>
            </div>
            <button
              className="dcbtn xs"
              onClick={() => {
                onToggle(item)
              }}
              type="button"
            >
              {item.enabled ? 'Pause' : 'Enable'}
            </button>
            <button
              className="dcbtn xs"
              onClick={() => {
                onEdit(item)
              }}
              type="button"
            >
              Edit
            </button>
            <button
              className="dcbtn danger xs"
              onClick={() => {
                onDelete(item)
              }}
              type="button"
            >
              Delete
            </button>
          </article>
        ))
      )}
    </section>
  )
}

function ClientDrawer({
  client,
  data,
  onClose,
  onCommand,
  projectId,
}: {
  client: CrmClient
  data: CrmPageModel
  onClose: () => void
  onCommand: (action: (signal: AbortSignal) => Promise<unknown>, success: string) => Promise<void>
  projectId: string
}) {
  const [tab, setTab] = useState<'overview' | 'communications' | 'artifacts'>('overview')
  const [channel, setChannel] = useState('Note')
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [chainId, setChainId] = useState('')
  const [assigned, setAssigned] = useState<Set<string>>(() => new Set())
  const detailQuery = useQuery(crmClientQueryOptions(projectId, client.id))
  const detail = detailQuery.data
  const effectiveAssigned = assigned.size === 0 ? new Set(detail?.assignedChainIds ?? []) : assigned
  return (
    <div className="crm-drawer-backdrop" onClick={onClose} role="presentation">
      <aside
        aria-label={`${client.name} details`}
        aria-modal="true"
        className="crm-drawer-react"
        onClick={(event) => {
          event.stopPropagation()
        }}
        role="dialog"
      >
        <header>
          <span className={client.lifecycleStage.toLowerCase()}>{initials(client.name)}</span>
          <div>
            <strong>{client.name}</strong>
            <small>
              {client.company ?? 'Individual client'} · {client.lifecycleStage}
            </small>
          </div>
          <button aria-label="Close client details" onClick={onClose} type="button">
            ×
          </button>
        </header>
        <nav>
          {(['overview', 'communications', 'artifacts'] as const).map((item) => (
            <button
              className={tab === item ? 'active' : undefined}
              key={item}
              onClick={() => {
                setTab(item)
              }}
              type="button"
            >
              {item}
            </button>
          ))}
        </nav>
        <div className="crm-drawer-body">
          {detailQuery.isPending ? <p>Loading customer workspace…</p> : null}
          {tab === 'overview' ? (
            <>
              <dl className="crm-client-facts">
                <div>
                  <dt>Email</dt>
                  <dd>{client.email ?? '—'}</dd>
                </div>
                <div>
                  <dt>Phone</dt>
                  <dd>{client.phone ?? '—'}</dd>
                </div>
                <div>
                  <dt>Updated</dt>
                  <dd>{formatDate(client.updatedAt)}</dd>
                </div>
              </dl>
              <label className="crm-stage-select">
                Lifecycle stage
                <select
                  value={client.lifecycleStage}
                  onChange={(event) =>
                    void onCommand(
                      (signal) => moveCrmClient(client.id, event.target.value, signal),
                      `${client.name} moved.`,
                    )
                  }
                >
                  {stages.map((item) => (
                    <option key={item}>{item}</option>
                  ))}
                </select>
              </label>
              <section className="dccard crm-detail-panel">
                <h3>Run an automation</h3>
                <div>
                  <select
                    onChange={(event) => {
                      setChainId(event.target.value)
                    }}
                    value={chainId}
                  >
                    <option value="">Select a job chain…</option>
                    {data.chains.map((chain) => (
                      <option key={chain.id} value={chain.id}>
                        {chain.name}
                      </option>
                    ))}
                  </select>
                  <button
                    className="dcbtn primary"
                    disabled={chainId === ''}
                    onClick={() =>
                      void onCommand(
                        (signal) => runCrmAutomation(client.id, chainId, signal),
                        'Automation run finished.',
                      )
                    }
                    type="button"
                  >
                    Run
                  </button>
                </div>
                {detail?.runs.map((run) => (
                  <p key={run.id}>
                    <b>{run.chainName}</b>
                    <span>
                      {run.status} · {formatDate(run.startedAt)}
                    </span>
                  </p>
                ))}
              </section>
              <section className="dccard crm-detail-panel">
                <h3>Automatic chain assignments</h3>
                {data.chains.map((chain) => (
                  <label key={chain.id}>
                    <input
                      checked={effectiveAssigned.has(chain.id)}
                      onChange={(event) => {
                        setAssigned((current) => {
                          const next = new Set(current.size === 0 ? effectiveAssigned : current)
                          if (event.target.checked) next.add(chain.id)
                          else next.delete(chain.id)
                          return next
                        })
                      }}
                      type="checkbox"
                    />
                    {chain.name}
                  </label>
                ))}
                <button
                  className="dcbtn"
                  onClick={() =>
                    void onCommand(
                      (signal) =>
                        setCrmChainAssignments(
                          projectId,
                          client.id,
                          [...effectiveAssigned],
                          signal,
                        ),
                      'Automation assignments saved.',
                    )
                  }
                  type="button"
                >
                  Save assignments
                </button>
              </section>
              <button
                className="dcbtn danger"
                onClick={() =>
                  void onCommand(
                    (signal) => deleteCrmClient(client.id, signal),
                    `${client.name} deleted.`,
                  ).then(onClose)
                }
                type="button"
              >
                Delete client
              </button>
            </>
          ) : null}
          {tab === 'communications' ? (
            <>
              <section className="crm-composer-react">
                <div>
                  {['Note', 'Email', 'Sms'].map((item) => (
                    <button
                      className={channel === item ? 'active' : undefined}
                      disabled={
                        (item === 'Email' && !data.capabilities.emailEnabled) ||
                        (item === 'Sms' && !data.capabilities.smsEnabled)
                      }
                      key={item}
                      onClick={() => {
                        setChannel(item)
                      }}
                      type="button"
                    >
                      {item}
                    </button>
                  ))}
                </div>
                {channel === 'Email' ? (
                  <input
                    aria-label="Subject"
                    onChange={(event) => {
                      setSubject(event.target.value)
                    }}
                    placeholder="Subject"
                    value={subject}
                  />
                ) : null}
                <textarea
                  aria-label="Message"
                  onChange={(event) => {
                    setBody(event.target.value)
                  }}
                  placeholder={channel === 'Note' ? 'Add an internal note…' : 'Write a message…'}
                  rows={5}
                  value={body}
                />
                <button
                  className="dcbtn primary"
                  disabled={body.trim() === '' || (channel === 'Email' && subject.trim() === '')}
                  onClick={() =>
                    void onCommand(
                      (signal) =>
                        sendCrmCommunication(client.id, { channel, subject, body }, signal),
                      channel === 'Note' ? 'Note added.' : 'Message sent.',
                    ).then(() => {
                      setBody('')
                      setSubject('')
                    })
                  }
                  type="button"
                >
                  {channel === 'Note' ? 'Add note' : `Send ${channel}`}
                </button>
              </section>
              <div className="crm-timeline-react">
                {detail?.communications.map((item) => (
                  <article key={item.id}>
                    <span>{item.channel}</span>
                    <div>
                      <strong>{item.subject ?? item.channel}</strong>
                      <p>{item.body}</p>
                      <small>
                        {formatDate(item.createdAt)} · {item.status}
                        {item.error === null ? '' : ` · ${item.error}`}
                      </small>
                    </div>
                  </article>
                ))}
              </div>
            </>
          ) : null}
          {tab === 'artifacts' ? (
            <section className="crm-artifacts-react">
              {detail?.artifacts.length === 0 ? (
                <p>No customer artifacts yet.</p>
              ) : (
                detail?.artifacts.map((artifact) => (
                  <article key={artifact.id}>
                    <span>◇</span>
                    <div>
                      <strong>{artifact.title}</strong>
                      <small>
                        {artifact.source} · {formatBytes(artifact.sizeBytes)} ·{' '}
                        {formatDate(artifact.createdAt)}
                      </small>
                    </div>
                    <a
                      className="dcbtn xs"
                      href={`/crm/artifacts/${artifact.id}`}
                      rel="noopener"
                      target="_blank"
                    >
                      Open ↗
                    </a>
                    <button
                      className="dcbtn danger xs"
                      onClick={() =>
                        void onCommand(
                          (signal) => removeCrmArtifact(client.id, artifact.id, signal),
                          'Artifact removed.',
                        )
                      }
                      type="button"
                    >
                      Remove
                    </button>
                  </article>
                ))
              )}
            </section>
          ) : null}
        </div>
      </aside>
    </div>
  )
}

function CrmDialog({
  data,
  editingId,
  kind,
  onClose,
  onSubmit,
  pending,
  projectId,
}: {
  data: CrmPageModel
  editingId: string | null
  kind: 'client' | 'appointment' | 'calendar' | 'automation' | 'portal'
  onClose: () => void
  onSubmit: (action: (signal: AbortSignal) => Promise<unknown>, success: string) => Promise<void>
  pending: boolean
  projectId: string
}) {
  const client = data.clients.find((item) => item.id === editingId)
  const automation = data.automations.find((item) => item.id === editingId)
  async function submit(event: SyntheticEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    const values = new FormData(event.currentTarget)
    const get = (name: string) => {
      const value = values.get(name)
      return typeof value === 'string' ? value.trim() : ''
    }
    const nullable = (name: string) => get(name) || null
    if (kind === 'client')
      await onSubmit(
        (signal) =>
          saveCrmClient(
            projectId,
            {
              projectId,
              name: get('name'),
              company: nullable('company'),
              email: nullable('email'),
              phone: nullable('phone'),
              lifecycleStage: get('lifecycleStage'),
              notes: nullable('notes'),
              clientId: client?.id ?? null,
            },
            signal,
          ),
        client === undefined ? 'Client added.' : 'Client updated.',
      )
    if (kind === 'appointment')
      await onSubmit(
        (signal) =>
          saveCrmAppointment(
            projectId,
            {
              projectId,
              calendarId: nullable('calendarId'),
              clientId: nullable('clientId'),
              title: get('title'),
              startsAt: new Date(get('startsAt')).toISOString(),
              endsAt: new Date(get('endsAt')).toISOString(),
              location: nullable('location'),
              notes: nullable('notes'),
              appointmentId: null,
            },
            signal,
          ),
        'Appointment saved.',
      )
    if (kind === 'calendar')
      await onSubmit(
        (signal) =>
          saveCrmCalendar(
            projectId,
            { projectId, name: get('name'), color: get('color'), calendarId: null },
            signal,
          ),
        'Calendar saved.',
      )
    if (kind === 'automation')
      await onSubmit(
        (signal) =>
          saveCrmAutomation(
            projectId,
            {
              projectId,
              name: get('name'),
              eventType: get('eventType'),
              lifecycleStage: nullable('lifecycleStage'),
              chainId: get('chainId'),
              enabled: true,
              ruleId: automation?.id ?? null,
            },
            signal,
          ),
        automation === undefined ? 'Automation created.' : 'Automation updated.',
      )
    if (kind === 'portal' && client !== undefined)
      await onSubmit(
        (signal) =>
          configureCrmPortal(
            client.id,
            {
              clientId: client.id,
              enabled: true,
              slug: nullable('slug'),
              domain: nullable('domain'),
              portalBrandName: nullable('brandName'),
              portalBrandLogoUrl: nullable('logoUrl'),
              defaultPortalUserName: null,
              defaultPortalUserEmail: null,
              defaultPortalUserPassword: null,
            },
            signal,
          ),
        'Customer portal configured.',
      )
  }
  return (
    <div className="modal-backdrop" onClick={onClose} role="presentation">
      <form
        aria-label={`${kind} editor`}
        className="modal crm-modal-react"
        onClick={(event) => {
          event.stopPropagation()
        }}
        onSubmit={(event) => void submit(event)}
      >
        <header className="modal-head">
          <div>
            <h3>
              {kind === 'portal'
                ? 'Configure customer portal'
                : `${editingId === null ? 'New' : 'Edit'} ${kind}`}
            </h3>
            <p>Changes apply to this project workspace.</p>
          </div>
          <button aria-label="Close" onClick={onClose} type="button">
            ×
          </button>
        </header>
        <div className="modal-body">
          {kind === 'client' ? (
            <>
              <label>
                Name
                <input defaultValue={client?.name} name="name" required />
              </label>
              <div className="crm-form-grid">
                <label>
                  Company
                  <input defaultValue={client?.company ?? ''} name="company" />
                </label>
                <label>
                  Lifecycle
                  <select defaultValue={client?.lifecycleStage ?? 'Lead'} name="lifecycleStage">
                    {stages.map((item) => (
                      <option key={item}>{item}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Email
                  <input defaultValue={client?.email ?? ''} name="email" type="email" />
                </label>
                <label>
                  Phone
                  <input defaultValue={client?.phone ?? ''} name="phone" />
                </label>
              </div>
              <label>
                Notes
                <textarea defaultValue={client?.notes ?? ''} name="notes" rows={5} />
              </label>
            </>
          ) : null}
          {kind === 'appointment' ? (
            <>
              <label>
                Title
                <input name="title" required />
              </label>
              <div className="crm-form-grid">
                <label>
                  Starts
                  <input name="startsAt" required type="datetime-local" />
                </label>
                <label>
                  Ends
                  <input name="endsAt" required type="datetime-local" />
                </label>
                <label>
                  Calendar
                  <select name="calendarId">
                    <option value="">Unassigned</option>
                    {data.calendars.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Client
                  <select name="clientId">
                    <option value="">Unassigned</option>
                    {data.clients.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <label>
                Location
                <input name="location" />
              </label>
              <label>
                Notes
                <textarea name="notes" rows={3} />
              </label>
            </>
          ) : null}
          {kind === 'calendar' ? (
            <div className="crm-form-grid">
              <label>
                Name
                <input name="name" required />
              </label>
              <label>
                Colour
                <input defaultValue="#4f7cff" name="color" type="color" />
              </label>
            </div>
          ) : null}
          {kind === 'automation' ? (
            <>
              <label>
                Name
                <input defaultValue={automation?.name} name="name" required />
              </label>
              <div className="crm-form-grid">
                <label>
                  Event
                  <select defaultValue={automation?.eventType ?? 'StageEntered'} name="eventType">
                    <option>StageEntered</option>
                    <option>ClientCreated</option>
                    <option>IngestionReceived</option>
                  </select>
                </label>
                <label>
                  Lifecycle stage
                  <select defaultValue={automation?.lifecycleStage ?? ''} name="lifecycleStage">
                    <option value="">Any stage</option>
                    {stages.map((item) => (
                      <option key={item}>{item}</option>
                    ))}
                  </select>
                </label>
              </div>
              <label>
                Job chain
                <select defaultValue={automation?.chainId} name="chainId" required>
                  <option value="">Select a chain…</option>
                  {data.chains.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
                </select>
              </label>
            </>
          ) : null}
          {kind === 'portal' ? (
            <>
              {client === undefined ? (
                <label>
                  Customer
                  <select name="client" onChange={() => undefined}>
                    <option>Select a customer from the portal list first</option>
                  </select>
                </label>
              ) : (
                <>
                  <p>
                    Portal access for <strong>{client.name}</strong>.
                  </p>
                  <label>
                    Customer slug
                    <input
                      defaultValue={client.customerPortalSlug ?? slugify(client.name)}
                      name="slug"
                    />
                  </label>
                  <label>
                    Custom domain
                    <input
                      defaultValue={client.customerPortalDomain ?? ''}
                      name="domain"
                      placeholder="portal.example.com"
                    />
                  </label>
                  <label>
                    Brand name
                    <input
                      defaultValue={client.customerPortalBrandName ?? client.name}
                      name="brandName"
                    />
                  </label>
                  <label>
                    Logo URL
                    <input
                      defaultValue={client.customerPortalLogoUrl ?? ''}
                      name="logoUrl"
                      type="url"
                    />
                  </label>
                </>
              )}
            </>
          ) : null}
        </div>
        <footer className="modal-actions">
          <button className="dcbtn" onClick={onClose} type="button">
            Cancel
          </button>
          <button
            className="dcbtn primary"
            disabled={pending || (kind === 'portal' && client === undefined)}
            type="submit"
          >
            {pending ? 'Saving…' : 'Save'}
          </button>
        </footer>
      </form>
    </div>
  )
}

function CrmSettings({ projectId }: { projectId: string }) {
  const queryClient = useQueryClient()
  const settingsQuery = useQuery({
    queryKey: ['crm-ingestion', projectId],
    queryFn: ({ signal }) => fetchCrmIngestionSettings(projectId, signal),
  })
  const [origin, setOrigin] = useState<string | null>(null)
  const [token, setToken] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const value = origin ?? settingsQuery.data?.allowedOrigin ?? ''
  async function execute(
    action: (signal: AbortSignal) => Promise<unknown>,
    success: string,
  ): Promise<void> {
    try {
      await action(AbortSignal.timeout(30_000))
      await queryClient.invalidateQueries({ queryKey: ['crm-ingestion', projectId] })
      setMessage(success)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Settings request failed.')
    }
  }
  return (
    <section className="dccard crm-settings-react">
      <header>
        <div>
          <strong>Lead form ingestion</strong>
          <small>Connect an external form without exposing the rest of CRM.</small>
        </div>
        <span className={settingsQuery.data?.enabled ? 'enabled' : undefined}>
          {settingsQuery.data?.enabled ? 'Enabled' : 'Disabled'}
        </span>
      </header>
      <div>
        <label>
          Allowed browser origin
          <input
            onChange={(event) => {
              setOrigin(event.target.value)
            }}
            placeholder="https://www.example.com"
            value={value}
          />
        </label>
        <small>Only browser requests from this exact origin can submit leads.</small>
        {settingsQuery.data?.tokenPrefix === null ||
        settingsQuery.data?.tokenPrefix === undefined ? null : (
          <p>
            Active token prefix <code>{settingsQuery.data.tokenPrefix}</code>
          </p>
        )}
        {token === null ? null : (
          <div className="crm-token-react">
            <strong>Copy this token now</strong>
            <code>{token}</code>
            <button
              className="dcbtn"
              onClick={() => void navigator.clipboard.writeText(token)}
              type="button"
            >
              Copy
            </button>
          </div>
        )}
        {message === null ? null : <p>{message}</p>}
        <footer>
          <button
            className="dcbtn"
            onClick={() =>
              void execute(
                (signal) => saveCrmIngestionOrigin(projectId, value, signal),
                'Allowed origin saved.',
              )
            }
            type="button"
          >
            Save origin
          </button>
          <button
            className="dcbtn primary"
            onClick={() =>
              void rotateCrmIngestionToken(projectId, value, AbortSignal.timeout(30_000)).then(
                async (result) => {
                  setToken(result.token)
                  await queryClient.invalidateQueries({ queryKey: ['crm-ingestion', projectId] })
                },
              )
            }
            type="button"
          >
            {settingsQuery.data?.enabled ? 'Rotate token' : 'Enable ingestion'}
          </button>
          {settingsQuery.data?.enabled ? (
            <button
              className="dcbtn danger"
              onClick={() =>
                void execute(
                  (signal) => disableCrmIngestion(projectId, signal),
                  'Lead ingestion disabled.',
                )
              }
              type="button"
            >
              Disable
            </button>
          ) : null}
        </footer>
      </div>
    </section>
  )
}

function sectionTitle(section: Section): string {
  return sections.find((item) => item.id === section)?.label ?? 'CRM'
}
function sectionDescription(section: Section): string {
  const descriptions: Record<Section, string> = {
    conversations: 'Keep customer email, SMS, and internal notes in one shared timeline.',
    calendars: 'Manage customer appointments and connected calendars.',
    contacts: 'Manage customer contact details and relationship context.',
    opportunities: 'Move customer opportunities through the full lifecycle pipeline.',
    automations: 'Connect lifecycle events to durable job-chain workflows.',
    portals: 'Configure portal access for selected CRM clients.',
    settings: 'Connect external lead forms without exposing the rest of CRM.',
  }
  return descriptions[section]
}
function sectionIcon(section: Section): string {
  return {
    conversations: '□',
    calendars: '▦',
    contacts: '♙',
    opportunities: '▥',
    automations: '⌁',
    portals: '◇',
    settings: '⚙',
  }[section]
}
function sectionCount(section: Section, data: CrmPageModel): number | null {
  if (section === 'contacts' || section === 'opportunities') return data.clients.length
  if (section === 'automations') return data.automations.length
  if (section === 'portals')
    return data.clients.filter((client) => client.customerPortalEnabled).length
  if (section === 'calendars') return data.appointments.length
  return null
}
function stageDescription(stage: string): string {
  return (
    (
      {
        Lead: 'New relationship',
        Qualified: 'Fit confirmed',
        Proposal: 'Offer in progress',
        Customer: 'Active customer',
        Churned: 'Closed relationship',
      } as Record<string, string>
    )[stage] ?? ''
  )
}
function initials(name: string): string {
  return name
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}
function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
}
function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  )
}
function formatBytes(value: number): string {
  return value >= 1_048_576
    ? `${(value / 1_048_576).toFixed(1)} MB`
    : value >= 1_024
      ? `${(value / 1_024).toFixed(1)} KB`
      : `${String(value)} B`
}
