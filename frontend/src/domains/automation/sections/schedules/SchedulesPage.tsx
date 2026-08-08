import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { createSchedule, deleteSchedule, updateSchedule } from '../../api/schedules-api'
import { schedulesQueryOptions } from '../../api/schedules-query'
import type { ScheduleTrigger } from '../../model/schedules'
type Kind = 'Schedule' | 'Event' | 'Launchpad'
type Command =
  | { kind: 'create'; body: object }
  | { kind: 'update'; id: string; body: object }
  | { kind: 'delete'; id: string }
export function SchedulesPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const options = schedulesQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [kind, setKind] = useState<Kind>('Schedule')
  const [name, setName] = useState('')
  const [jobId, setJobId] = useState(data.jobs[0]?.id ?? '')
  const [chainId, setChainId] = useState(data.chains[0]?.id ?? '')
  const [sourceTable, setSourceTable] = useState('')
  const [eventName, setEventName] = useState(data.eventTypes[0] ?? '')
  const [prompt, setPrompt] = useState('')
  const [cron, setCron] = useState('0 9 * * *')
  const [advanced, setAdvanced] = useState(false)
  const [frequency, setFrequency] = useState('day')
  const [weekday, setWeekday] = useState('1')
  const [monthDay, setMonthDay] = useState(1)
  const [time, setTime] = useState('09:00')
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<ScheduleTrigger | null>(null)
  const [editName, setEditName] = useState('')
  const [editFire, setEditFire] = useState('')
  const mutation = useMutation({
    mutationFn: async (command: Command) => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'create') return createSchedule(projectId, command.body, signal)
      if (command.kind === 'update')
        return updateSchedule(projectId, command.id, command.body, signal)
      await deleteSchedule(projectId, command.id, signal)
      return null
    },
    onSuccess: async (_result, command) => {
      if (command.kind === 'create') {
        setName('')
        setPrompt('')
        setSourceTable('')
      }
      if (command.kind === 'update') setEditing(null)
      await client.invalidateQueries({ queryKey: options.queryKey })
    },
  })
  function selectedCron(): string {
    if (advanced) return cron
    const [hour = '9', minute = '0'] = time.split(':')
    if (frequency === 'hour') return '0 * * * *'
    if (frequency === 'weekday') return `${minute} ${hour} * * 1-5`
    if (frequency === 'week') return `${minute} ${hour} * * ${weekday}`
    if (frequency === 'month')
      return `${minute} ${hour} ${String(Number.isFinite(monthDay) ? Math.min(28, Math.max(1, monthDay)) : 1)} * *`
    return `${minute} ${hour} * * *`
  }
  async function execute(command: Command): Promise<void> {
    setError(null)
    try {
      await mutation.mutateAsync(command)
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : 'The schedule could not be updated.')
    }
  }
  async function add(): Promise<void> {
    if (name.trim() === '') {
      setError('Name is required.')
      return
    }
    if (kind === 'Launchpad' && chainId === '') {
      setError('Chain is required.')
      return
    }
    if (kind === 'Launchpad' && prompt.trim() === '') {
      setError('Prompt is required.')
      return
    }
    if (kind !== 'Launchpad' && jobId === '') {
      setError('Job is required.')
      return
    }
    await execute({
      kind: 'create',
      body: {
        name: name.trim(),
        kind,
        jobId: kind === 'Launchpad' ? null : jobId,
        chainId: kind === 'Launchpad' ? chainId : null,
        cronExpression: kind === 'Event' ? null : selectedCron(),
        eventName: kind === 'Event' ? eventName : null,
        sourceTable: kind === 'Launchpad' && sourceTable !== '' ? sourceTable : null,
        prompt: kind === 'Launchpad' ? prompt.trim() : null,
      },
    })
  }
  async function startEdit(item: ScheduleTrigger): Promise<void> {
    await Promise.resolve()
    setEditing(item)
    setEditName(item.name)
    setEditFire(item.kind === 'Event' ? (item.eventName ?? '') : (item.cronExpression ?? ''))
  }
  return (
    <div className="schedules-page">
      <title>placecontext — Schedules</title>
      <header>
        <h1>Schedules</h1>
        <p>
          Every trigger across this project's jobs — cron schedules, event hooks, and launchpads.
          Cron fires in <strong>{data.timeZoneId}</strong>.
        </p>
      </header>
      {error === null ? null : (
        <div className="error-banner" role="alert">
          {error}
        </div>
      )}
      <section className="dccard schedule-add">
        <label>
          Kind
          <select
            value={kind}
            onChange={(event) => {
              setKind(event.target.value as Kind)
            }}
          >
            <option>Schedule</option>
            <option>Event</option>
            <option>Launchpad</option>
          </select>
        </label>
        <label>
          Name
          <input
            value={name}
            onChange={(event) => {
              setName(event.target.value)
            }}
            placeholder="nightly refresh"
          />
        </label>
        {kind === 'Launchpad' ? (
          <>
            <label>
              Chain
              <select
                value={chainId}
                onChange={(event) => {
                  setChainId(event.target.value)
                }}
              >
                {data.chains.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Source table
              <select
                value={sourceTable}
                onChange={(event) => {
                  setSourceTable(event.target.value)
                }}
              >
                <option value="">— optional —</option>
                {data.tables.map((table) => (
                  <option key={table}>{table}</option>
                ))}
              </select>
            </label>
          </>
        ) : (
          <label>
            Job
            <select
              value={jobId}
              onChange={(event) => {
                setJobId(event.target.value)
              }}
            >
              {data.jobs.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
        )}
        {kind === 'Event' ? (
          <label>
            Event
            <select
              value={eventName}
              onChange={(event) => {
                setEventName(event.target.value)
              }}
            >
              {data.eventTypes.map((event) => (
                <option key={event}>{event}</option>
              ))}
            </select>
          </label>
        ) : advanced ? (
          <label>
            Cron
            <input
              value={cron}
              onChange={(event) => {
                setCron(event.target.value)
              }}
            />
          </label>
        ) : (
          <>
            <label>
              Every
              <select
                value={frequency}
                onChange={(event) => {
                  setFrequency(event.target.value)
                }}
              >
                <option value="day">day</option>
                <option value="weekday">weekday</option>
                <option value="week">week</option>
                <option value="month">month</option>
                <option value="hour">hour</option>
              </select>
            </label>
            {frequency === 'week' ? (
              <label>
                On
                <select
                  value={weekday}
                  onChange={(event) => {
                    setWeekday(event.target.value)
                  }}
                >
                  <option value="1">Monday</option>
                  <option value="2">Tuesday</option>
                  <option value="3">Wednesday</option>
                  <option value="4">Thursday</option>
                  <option value="5">Friday</option>
                  <option value="6">Saturday</option>
                  <option value="0">Sunday</option>
                </select>
              </label>
            ) : null}
            {frequency === 'month' ? (
              <label>
                Day
                <input
                  max={28}
                  min={1}
                  type="number"
                  value={monthDay}
                  onChange={(event) => {
                    setMonthDay(event.target.valueAsNumber)
                  }}
                />
              </label>
            ) : null}
            {frequency === 'hour' ? null : (
              <label>
                At
                <input
                  type="time"
                  value={time}
                  onChange={(event) => {
                    setTime(event.target.value)
                  }}
                />
              </label>
            )}
          </>
        )}
        {kind === 'Event' ? null : (
          <button
            onClick={() => {
              setAdvanced((value) => !value)
            }}
            type="button"
          >
            {advanced ? '◂ simple' : 'cron ▸'}
          </button>
        )}
        {kind === 'Launchpad' ? (
          <label className="prompt">
            Prompt
            <textarea
              rows={3}
              value={prompt}
              onChange={(event) => {
                setPrompt(event.target.value)
              }}
            />
          </label>
        ) : null}
        <button
          className="dcbtn primary"
          disabled={mutation.isPending}
          onClick={() => void add()}
          type="button"
        >
          ＋ Add trigger
        </button>
      </section>
      <section className="dccard schedule-table">
        <div className="schedule-row head">
          <span>STATE</span>
          <span>TRIGGER</span>
          <span>TARGET</span>
          <span>FIRES ON</span>
          <span>NEXT RUN</span>
          <span>LAST FIRED</span>
          <span />
        </div>
        {data.triggers.length === 0 ? (
          <p>No triggers yet — add a cron schedule, an event hook, or an agent launchpad above.</p>
        ) : (
          data.triggers.map((item) =>
            editing?.id === item.id ? (
              <div className="schedule-row" key={item.id}>
                <span>{item.enabled ? 'ON' : 'PAUSED'}</span>
                <input
                  value={editName}
                  onChange={(event) => {
                    setEditName(event.target.value)
                  }}
                />
                <span>{item.targetLabel}</span>
                <input
                  value={editFire}
                  onChange={(event) => {
                    setEditFire(event.target.value)
                  }}
                />
                <span>{item.nextRunLabel}</span>
                <span>{item.lastFiredLabel}</span>
                <div>
                  <button
                    onClick={() =>
                      void execute({
                        kind: 'update',
                        id: item.id,
                        body: {
                          name: editName,
                          cronExpression: item.kind === 'Event' ? null : editFire,
                          eventName: item.kind === 'Event' ? editFire : null,
                          enabled: null,
                        },
                      })
                    }
                    type="button"
                  >
                    Save
                  </button>
                  <button
                    onClick={() => {
                      setEditing(null)
                    }}
                    type="button"
                  >
                    Cancel
                  </button>
                </div>
              </div>
            ) : (
              <div className="schedule-row" key={item.id}>
                <span className={item.enabled ? 'on' : ''}>{item.enabled ? 'ON' : 'PAUSED'}</span>
                <strong>{item.name}</strong>
                <span>{item.targetLabel}</span>
                <span>
                  {item.kind === 'Event'
                    ? `event ${item.eventName ?? ''}`
                    : `cron ${item.cronExpression ?? ''}`}
                </span>
                <span>{item.nextRunLabel}</span>
                <span>{item.lastFiredLabel}</span>
                <div>
                  <button
                    onClick={() =>
                      void execute({
                        kind: 'update',
                        id: item.id,
                        body: { enabled: !item.enabled },
                      })
                    }
                    type="button"
                  >
                    {item.enabled ? 'Pause' : 'Resume'}
                  </button>
                  <button onClick={() => void startEdit(item)} type="button">
                    Edit
                  </button>
                  <button
                    onClick={() => void execute({ kind: 'delete', id: item.id })}
                    type="button"
                  >
                    Delete
                  </button>
                </div>
              </div>
            ),
          )
        )}
      </section>
    </div>
  )
}
