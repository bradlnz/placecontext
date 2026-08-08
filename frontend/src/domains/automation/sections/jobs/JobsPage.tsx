import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { createJob, deleteJob, runJob, updateJob } from '../../api/jobs-api'
import { jobRunsQueryOptions, jobsQueryOptions } from '../../api/jobs-query'
import type { Job, JobRequestBody, JobsPageModel } from '../../model/jobs'

interface Draft {
  id: string | null
  name: string
  description: string
  sourceKind: 'image' | 'code'
  image: string
  runtime: string
  source: string
  entrypoint: string
  payloads: string
  env: string
  concurrency: number
  successCodes: string
  partialCodes: string
  network: boolean
  api: boolean
  returnType: string
  returnFileName: string
  retryCount: number
  retryDelaySeconds: number
  original: Job | null
}
type Command =
  | { kind: 'save'; draft: Draft }
  | { kind: 'delete'; id: string }
  | { kind: 'run'; id: string; payload: string | null }
const emptyDraft = (): Draft => ({
  id: null,
  name: '',
  description: '',
  sourceKind: 'image',
  image: '',
  runtime: 'node',
  source: '',
  entrypoint: '',
  payloads: '{}',
  env: '',
  concurrency: 1,
  successCodes: '0',
  partialCodes: '',
  network: false,
  api: false,
  returnType: 'Json',
  returnFileName: '',
  retryCount: 0,
  retryDelaySeconds: 0,
  original: null,
})
function editDraft(job: Job): Draft {
  return {
    id: job.id,
    name: job.name,
    description: job.description ?? '',
    sourceKind: job.mapSourceKind === 'code' ? 'code' : 'image',
    image: job.mapImage ?? '',
    runtime: job.mapRuntimeId ?? 'node',
    source:
      job.mapSource ?? job.mapFiles.find((file) => file.path === job.mapEntrypoint)?.content ?? '',
    entrypoint: job.mapEntrypoint ?? '',
    payloads: job.inputPayloads.join('\n'),
    env: Object.entries(job.mapEnv)
      .map(([key, value]) => `${key}=${value}`)
      .join('\n'),
    concurrency: job.concurrencyLimit,
    successCodes: job.successExitCodes.join(','),
    partialCodes: job.partialExitCodes.join(','),
    network: job.allowNetworkEgress,
    api: job.allowApiInvocation,
    returnType: job.returnType,
    returnFileName: job.returnFileName ?? '',
    retryCount: job.retryCount,
    retryDelaySeconds: job.retryDelaySeconds,
    original: job,
  }
}
function parseIntegers(value: string): number[] {
  if (value.trim() === '') return []
  const result = value.split(',').map((part) => Number.parseInt(part.trim(), 10))
  if (result.some((item) => !Number.isInteger(item)))
    throw new Error('Exit codes must be comma-separated integers.')
  return result
}
function request(draft: Draft): JobRequestBody {
  const original = draft.original
  const mapEnv: Record<string, string> = {}
  for (const line of draft.env
    .split('\n')
    .map((item) => item.trim())
    .filter(Boolean)) {
    const index = line.indexOf('=')
    if (index > 0) mapEnv[line.slice(0, index).trim()] = line.slice(index + 1)
  }
  const mapFiles =
    draft.sourceKind === 'code' && original !== null && original.mapFiles.length > 1
      ? original.mapFiles.map((file) =>
          file.path === (draft.entrypoint || original.mapEntrypoint)
            ? { ...file, content: draft.source }
            : file,
        )
      : null
  return {
    name: draft.name.trim(),
    description: draft.description.trim() || null,
    mapImage: draft.sourceKind === 'image' ? draft.image.trim() : null,
    mapRuntimeId: draft.sourceKind === 'code' ? draft.runtime : null,
    mapSource: draft.sourceKind === 'code' && mapFiles === null ? draft.source : null,
    mapEntrypoint: draft.sourceKind === 'code' ? draft.entrypoint.trim() || null : null,
    mapFiles,
    inputPayloads: draft.payloads
      .split('\n')
      .map((item) => item.trim())
      .filter(Boolean),
    mapEnv,
    reduceImage: original?.reduceImage ?? null,
    reduceRuntimeId: original?.reduceRuntimeId ?? null,
    reduceSource: original?.reduceSource ?? null,
    reduceEntrypoint: original?.reduceEntrypoint ?? null,
    reduceFiles: original?.reduceFiles ?? null,
    reduceEnv: original?.reduceEnv ?? null,
    concurrencyLimit: draft.concurrency,
    successExitCodes: parseIntegers(draft.successCodes),
    partialExitCodes: parseIntegers(draft.partialCodes),
    allowNetworkEgress: draft.network,
    allowApiInvocation: draft.api,
    parameters: original?.parameters ?? [],
    postJobActions: original?.postJobActions ?? [],
    returnType: draft.returnType,
    returnFileName: draft.returnFileName.trim() || null,
    retryCount: draft.retryCount,
    retryDelaySeconds: draft.retryDelaySeconds,
    mcpConnectionIds: original?.mcpConnectionIds ?? [],
  }
}
function replaceJob(model: JobsPageModel | undefined, job: Job): JobsPageModel | undefined {
  if (model === undefined) return model
  return {
    ...model,
    jobs: model.jobs.some((item) => item.id === job.id)
      ? model.jobs.map((item) => (item.id === job.id ? job : item))
      : [...model.jobs, job],
  }
}

export function JobsPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const options = jobsQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [draft, setDraft] = useState<Draft | null>(null)
  const [tab, setTab] = useState<'details' | 'runs' | 'triggers'>('details')
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [message, setMessage] = useState<{
    text: string
    error: boolean
  } | null>(null)
  const [runPrompt, setRunPrompt] = useState<Job | null>(null)
  const [runPayload, setRunPayload] = useState('{}')
  const runs = useQuery({
    ...jobRunsQueryOptions(projectId, draft?.id ?? ''),
    enabled: draft?.id !== null && draft?.id !== undefined && tab === 'runs',
  })
  const mutation = useMutation({
    mutationFn: async (command: Command) => {
      if (command.kind === 'delete') {
        await deleteJob(projectId, command.id, AbortSignal.timeout(30_000))
        return null
      }
      if (command.kind === 'run')
        return runJob(projectId, command.id, command.payload, AbortSignal.timeout(120_000))
      const body = request(command.draft)
      return command.draft.id === null
        ? createJob(projectId, body, AbortSignal.timeout(30_000))
        : updateJob(projectId, command.draft.id, body, AbortSignal.timeout(30_000))
    },
  })
  const activeTriggers = data.triggers.filter((trigger) => trigger.enabled)
  const automated = data.jobs.filter((job) =>
    activeTriggers.some((trigger) => trigger.jobId === job.id),
  ).length
  async function save(): Promise<void> {
    if (draft === null) return
    if (draft.name.trim() === '') {
      setMessage({ text: 'Name is required.', error: true })
      return
    }
    if (draft.payloads.trim() === '') {
      setMessage({
        text: 'At least one input payload is required.',
        error: true,
      })
      return
    }
    setMessage(null)
    try {
      const result = await mutation.mutateAsync({ kind: 'save', draft })
      if (result !== null && 'name' in result)
        client.setQueryData(options.queryKey, (old: JobsPageModel | undefined) =>
          replaceJob(old, result),
        )
      setDraft(null)
      setMessage({
        text: `Job '${draft.name.trim()}' ${draft.id === null ? 'created' : 'updated'}.`,
        error: false,
      })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The job could not be saved.',
        error: true,
      })
    }
  }
  async function remove(id: string): Promise<void> {
    try {
      await mutation.mutateAsync({ kind: 'delete', id })
      client.setQueryData<JobsPageModel>(options.queryKey, (old) =>
        old === undefined ? old : { ...old, jobs: old.jobs.filter((job) => job.id !== id) },
      )
      setMessage({ text: 'Job deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The job could not be deleted.',
        error: true,
      })
    } finally {
      setConfirmDelete(null)
    }
  }
  function beginRun(job: Job): void {
    setRunPrompt(job)
    setRunPayload(job.inputPayloads[0] ?? '{}')
  }
  async function executeRun(): Promise<void> {
    if (runPrompt === null) return
    setMessage(null)
    try {
      const result = await mutation.mutateAsync({
        kind: 'run',
        id: runPrompt.id,
        payload: runPrompt.parameters.length > 0 ? runPayload : null,
      })
      const status = result !== null && 'status' in result ? result.status : 'started'
      setMessage({ text: `Run ${status}.`, error: false })
      setRunPrompt(null)
      if (draft?.id === runPrompt.id) {
        setTab('runs')
        await runs.refetch()
      }
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The job could not be run.',
        error: true,
      })
    }
  }
  function open(job: Job): void {
    setDraft(editDraft(job))
    setTab('details')
  }

  return (
    <div className="jobs-page-react">
      <title>PlaceContext — Jobs</title>
      <header className="jobs-head-react">
        <div>
          <h1>Jobs</h1>
          <p>
            Build reusable workloads, run them on demand, and connect them to automated triggers.
          </p>
        </div>
        <div>
          <button
            className="dcbtn"
            onClick={() => {
              void navigate(`/project/${projectId}/tests`)
            }}
            type="button"
          >
            View tests
          </button>
          <button
            className="dcbtn primary"
            onClick={() => {
              setDraft(emptyDraft())
            }}
            type="button"
          >
            ＋ New job
          </button>
        </div>
      </header>
      {message === null ? null : (
        <div
          className={message.error ? 'jobs-message-react error' : 'jobs-message-react'}
          role={message.error ? 'alert' : 'status'}
        >
          {message.text}
        </div>
      )}
      {data.jobs.length === 0 ? (
        <section className="dccard jobs-empty-react">
          <strong>No jobs yet</strong>
          <span>
            Create a reusable workload, then run it manually or connect it to a schedule or event.
          </span>
          <button
            className="dcbtn primary"
            onClick={() => {
              setDraft(emptyDraft())
            }}
            type="button"
          >
            Create first job
          </button>
        </section>
      ) : (
        <>
          <section aria-label="Job summary" className="jobs-summary-react">
            <div>
              <strong>{data.jobs.length}</strong>
              <span>jobs</span>
            </div>
            <div>
              <strong>{data.jobs.reduce((sum, job) => sum + job.inputPayloads.length, 0)}</strong>
              <span>shards</span>
            </div>
            <div>
              <strong>{automated}</strong>
              <span>automated</span>
            </div>
            <div>
              <strong>{activeTriggers.length}</strong>
              <span>active triggers</span>
            </div>
            <i>
              <span
                style={{
                  width: `${String(data.jobs.length === 0 ? 0 : Math.round((automated * 100) / data.jobs.length))}%`,
                }}
              />
            </i>
          </section>
          <section className="dccard jobs-catalog-react">
            <header>
              <div>
                <strong>Job catalogue</strong>
                <span>
                  {data.jobs.length} reusable {data.jobs.length === 1 ? 'workload' : 'workloads'}
                </span>
              </div>
              <span>{automated} automated</span>
            </header>
            {data.jobs.map((job) => {
              const triggerCount = activeTriggers.filter(
                (trigger) => trigger.jobId === job.id,
              ).length
              const running =
                mutation.isPending &&
                mutation.variables.kind === 'run' &&
                mutation.variables.id === job.id
              return (
                <article
                  className="job-row-react"
                  key={job.id}
                  onClick={() => {
                    open(job)
                  }}
                >
                  <span>{running ? '◌' : job.mapSourceKind === 'code' ? '⌁' : '□'}</span>
                  <div>
                    <div>
                      <strong>{job.name}</strong>
                      <small>
                        {job.mapSourceKind === 'code'
                          ? `${job.mapRuntimeId ?? 'code'} code`
                          : 'container'}
                      </small>
                      <small>{job.returnType} output</small>
                      {job.reduceSourceKind === null ? null : <small>reduce step</small>}
                      {triggerCount === 0 ? null : (
                        <small>
                          {triggerCount} active {triggerCount === 1 ? 'trigger' : 'triggers'}
                        </small>
                      )}
                    </div>
                    <p>
                      {job.description ?? 'No description'}
                      <span>
                        {job.inputPayloads.length} shards · concurrency {job.concurrencyLimit}
                        {job.allowNetworkEgress ? ' · network' : ''}
                      </span>
                    </p>
                  </div>
                  <div
                    onClick={(event) => {
                      event.stopPropagation()
                    }}
                  >
                    {confirmDelete === job.id ? (
                      <>
                        <button
                          className="dcbtn danger"
                          onClick={() => void remove(job.id)}
                          type="button"
                        >
                          Delete
                        </button>
                        <button
                          className="dcbtn"
                          onClick={() => {
                            setConfirmDelete(null)
                          }}
                          type="button"
                        >
                          Keep
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          aria-label={`Run ${job.name}`}
                          onClick={() => {
                            beginRun(job)
                          }}
                          type="button"
                        >
                          ▶
                        </button>
                        <button
                          aria-label={`Edit ${job.name}`}
                          onClick={() => {
                            open(job)
                          }}
                          type="button"
                        >
                          ✎
                        </button>
                        {job.mapSourceKind === 'code' ? (
                          <button
                            aria-label={`Open code editor for ${job.name}`}
                            onClick={() => {
                              void navigate(`/project/${projectId}/jobs/${job.id}`)
                            }}
                            type="button"
                          >
                            &lt;/&gt;
                          </button>
                        ) : null}
                        <button
                          aria-label={`Delete ${job.name}`}
                          onClick={() => {
                            setConfirmDelete(job.id)
                          }}
                          type="button"
                        >
                          ⋯
                        </button>
                      </>
                    )}
                  </div>
                </article>
              )
            })}
          </section>
        </>
      )}
      {draft === null ? null : (
        <div
          className="job-drawer-backdrop"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) setDraft(null)
          }}
        >
          <aside className="job-drawer-react">
            <header>
              <div>
                <strong>{draft.id === null ? 'New job' : draft.name}</strong>
                <span>
                  A job fans input payloads out as parallel shards and collects a result from each.
                </span>
              </div>
              {draft.id === null ? null : (
                <button
                  className="dcbtn primary"
                  onClick={() => {
                    const job = data.jobs.find((item) => item.id === draft.id)
                    if (job !== undefined) beginRun(job)
                  }}
                  type="button"
                >
                  ▶ Run
                </button>
              )}
              <button
                aria-label="Close"
                onClick={() => {
                  setDraft(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            {draft.id === null ? null : (
              <nav>
                <button
                  className={tab === 'details' ? 'active' : ''}
                  onClick={() => {
                    setTab('details')
                  }}
                  type="button"
                >
                  Details
                </button>
                <button
                  className={tab === 'runs' ? 'active' : ''}
                  onClick={() => {
                    setTab('runs')
                  }}
                  type="button"
                >
                  Runs
                </button>
                <button
                  className={tab === 'triggers' ? 'active' : ''}
                  onClick={() => {
                    setTab('triggers')
                  }}
                  type="button"
                >
                  Triggers
                </button>
              </nav>
            )}
            <div className="job-drawer-body">
              {tab === 'runs' ? (
                runs.isLoading ? (
                  <p>Loading runs…</p>
                ) : runs.data?.length === 0 ? (
                  <p>No runs yet — click Run to start one.</p>
                ) : (
                  <div className="job-run-list-react">
                    {runs.data?.map((run) => (
                      <article className="dccard" key={run.id}>
                        <strong className={run.status.toLocaleLowerCase()}>{run.status}</strong>
                        <span>{run.startedAtDisplay}</span>
                        <small>
                          ✓ {run.succeededShards}{' '}
                          {run.partialShards > 0 ? `~ ${String(run.partialShards)}` : ''}{' '}
                          {run.failedShards > 0 ? `✗ ${String(run.failedShards)}` : ''}
                        </small>
                        <code>{run.durationDisplay}</code>
                      </article>
                    ))}
                  </div>
                )
              ) : tab === 'triggers' ? (
                <div className="job-trigger-list-react">
                  <p>Schedules and event hooks attached to this job.</p>
                  {data.triggers
                    .filter((trigger) => trigger.jobId === draft.id)
                    .map((trigger) => (
                      <article className="dccard" key={trigger.id}>
                        <strong>{trigger.name}</strong>
                        <span>{trigger.kind}</span>
                        <code>
                          {trigger.kind === 'Event' ? trigger.eventName : trigger.cronExpression}
                        </code>
                        <small>{trigger.enabled ? 'active' : 'paused'}</small>
                      </article>
                    ))}
                  <button
                    className="dcbtn"
                    onClick={() => {
                      void navigate(`/project/${projectId}/schedules`)
                    }}
                    type="button"
                  >
                    Manage schedules
                  </button>
                </div>
              ) : (
                <div className="job-form-react">
                  <label>
                    Name
                    <input
                      className="dcinput"
                      onChange={(event) => {
                        setDraft({ ...draft, name: event.target.value })
                      }}
                      value={draft.name}
                    />
                  </label>
                  <label>
                    Description
                    <textarea
                      className="dcinput"
                      onChange={(event) => {
                        setDraft({ ...draft, description: event.target.value })
                      }}
                      rows={2}
                      value={draft.description}
                    />
                  </label>
                  <fieldset>
                    <legend>Workload source</legend>
                    <div>
                      <button
                        className={draft.sourceKind === 'image' ? 'active' : ''}
                        onClick={() => {
                          setDraft({ ...draft, sourceKind: 'image' })
                        }}
                        type="button"
                      >
                        Container image
                      </button>
                      <button
                        className={draft.sourceKind === 'code' ? 'active' : ''}
                        onClick={() => {
                          setDraft({ ...draft, sourceKind: 'code' })
                        }}
                        type="button"
                      >
                        Inline code
                      </button>
                    </div>
                    {draft.sourceKind === 'image' ? (
                      <label>
                        Image
                        <input
                          className="dcinput"
                          onChange={(event) => {
                            setDraft({ ...draft, image: event.target.value })
                          }}
                          placeholder="alpine:3.20"
                          value={draft.image}
                        />
                      </label>
                    ) : (
                      <>
                        <div className="job-form-grid">
                          <label>
                            Runtime
                            <select
                              className="dcinput"
                              onChange={(event) => {
                                setDraft({
                                  ...draft,
                                  runtime: event.target.value,
                                })
                              }}
                              value={draft.runtime}
                            >
                              <option value="node">Node.js</option>
                              <option value="python">Python</option>
                              <option value="go">Go</option>
                              <option value="ruby">Ruby</option>
                            </select>
                          </label>
                          <label>
                            Entrypoint
                            <input
                              className="dcinput"
                              onChange={(event) => {
                                setDraft({
                                  ...draft,
                                  entrypoint: event.target.value,
                                })
                              }}
                              placeholder="index.js"
                              value={draft.entrypoint}
                            />
                          </label>
                        </div>
                        <label>
                          Source
                          <textarea
                            className="dcinput job-source-react"
                            onChange={(event) => {
                              setDraft({
                                ...draft,
                                source: event.target.value,
                              })
                            }}
                            rows={10}
                            spellCheck={false}
                            value={draft.source}
                          />
                        </label>
                        {draft.id === null ? null : (
                          <button
                            className="dcbtn"
                            onClick={() => {
                              void navigate(`/project/${projectId}/jobs/${draft.id ?? ''}`)
                            }}
                            type="button"
                          >
                            Open multi-file editor
                          </button>
                        )}
                      </>
                    )}
                  </fieldset>
                  <fieldset>
                    <legend>Inputs & execution</legend>
                    <label>
                      Input payloads · one JSON value per line
                      <textarea
                        className="dcinput"
                        onChange={(event) => {
                          setDraft({ ...draft, payloads: event.target.value })
                        }}
                        rows={4}
                        spellCheck={false}
                        value={draft.payloads}
                      />
                    </label>
                    <label>
                      Environment · KEY=value per line
                      <textarea
                        className="dcinput"
                        onChange={(event) => {
                          setDraft({ ...draft, env: event.target.value })
                        }}
                        rows={4}
                        spellCheck={false}
                        value={draft.env}
                      />
                    </label>
                    <div className="job-form-grid">
                      <label>
                        Concurrency
                        <input
                          className="dcinput"
                          min={1}
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              concurrency: event.target.valueAsNumber,
                            })
                          }}
                          type="number"
                          value={draft.concurrency}
                        />
                      </label>
                      <label>
                        Success exit codes
                        <input
                          className="dcinput"
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              successCodes: event.target.value,
                            })
                          }}
                          value={draft.successCodes}
                        />
                      </label>
                      <label>
                        Partial exit codes
                        <input
                          className="dcinput"
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              partialCodes: event.target.value,
                            })
                          }}
                          value={draft.partialCodes}
                        />
                      </label>
                      <label>
                        Return type
                        <select
                          className="dcinput"
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              returnType: event.target.value,
                            })
                          }}
                          value={draft.returnType}
                        >
                          {[
                            'Json',
                            'Table',
                            'Chart',
                            'Html',
                            'Csv',
                            'Text',
                            'Pdf',
                            'Image',
                            'Video',
                          ].map((type) => (
                            <option key={type}>{type}</option>
                          ))}
                        </select>
                      </label>
                      <label>
                        Retry count
                        <input
                          className="dcinput"
                          min={0}
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              retryCount: event.target.valueAsNumber,
                            })
                          }}
                          type="number"
                          value={draft.retryCount}
                        />
                      </label>
                      <label>
                        Retry delay seconds
                        <input
                          className="dcinput"
                          min={0}
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              retryDelaySeconds: event.target.valueAsNumber,
                            })
                          }}
                          type="number"
                          value={draft.retryDelaySeconds}
                        />
                      </label>
                    </div>
                    {['Pdf', 'Image', 'Video'].includes(draft.returnType) ? (
                      <label>
                        Output file
                        <input
                          className="dcinput"
                          onChange={(event) => {
                            setDraft({
                              ...draft,
                              returnFileName: event.target.value,
                            })
                          }}
                          value={draft.returnFileName}
                        />
                      </label>
                    ) : null}
                    <label className="job-check-react">
                      <input
                        checked={draft.network}
                        onChange={(event) => {
                          setDraft({ ...draft, network: event.target.checked })
                        }}
                        type="checkbox"
                      />
                      Allow outbound network access
                    </label>
                    <label className="job-check-react">
                      <input
                        checked={draft.api}
                        onChange={(event) => {
                          setDraft({ ...draft, api: event.target.checked })
                        }}
                        type="checkbox"
                      />
                      Allow API invocation
                    </label>
                  </fieldset>
                </div>
              )}
            </div>
            {tab === 'details' ? (
              <footer>
                <button
                  className="dcbtn"
                  onClick={() => {
                    setDraft(null)
                  }}
                  type="button"
                >
                  Cancel
                </button>
                <button
                  className="dcbtn primary"
                  disabled={mutation.isPending}
                  onClick={() => void save()}
                  type="button"
                >
                  {mutation.isPending ? 'Saving…' : 'Save job'}
                </button>
              </footer>
            ) : null}
          </aside>
        </div>
      )}
      {runPrompt === null ? null : (
        <div className="job-run-modal">
          <section className="dccard" role="dialog" aria-label={`Run ${runPrompt.name}`}>
            <header>
              <strong>Run {runPrompt.name}</strong>
              <button
                aria-label="Close"
                onClick={() => {
                  setRunPrompt(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            {runPrompt.parameters.length > 0 ? (
              <label>
                Input payload
                <textarea
                  className="dcinput"
                  onChange={(event) => {
                    setRunPayload(event.target.value)
                  }}
                  rows={8}
                  spellCheck={false}
                  value={runPayload}
                />
              </label>
            ) : (
              <p>This job will run with its saved input payloads.</p>
            )}
            <footer>
              <button
                className="dcbtn"
                onClick={() => {
                  setRunPrompt(null)
                }}
                type="button"
              >
                Cancel
              </button>
              <button
                className="dcbtn primary"
                disabled={mutation.isPending}
                onClick={() => void executeRun()}
                type="button"
              >
                {mutation.isPending ? 'Running…' : 'Run job'}
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  )
}
