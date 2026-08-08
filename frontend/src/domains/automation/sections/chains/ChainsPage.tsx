import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { createChain, deleteChain, runChain, updateChain } from '../../api/job-chains-api'
import { chainRunsQueryOptions, chainsQueryOptions } from '../../api/job-chains-query'
import type {
  ChainAction,
  ChainGate,
  ChainStage,
  JobChain,
  JobChainsPageModel,
  SaveChainBody,
} from '../../model/job-chains'

interface DraftStage {
  jobIds: string[]
  gate: ChainGate | null
  action: ChainAction | null
}
interface Draft {
  id: string | null
  name: string
  description: string
  stages: DraftStage[]
}
type Command =
  | { kind: 'save'; draft: Draft }
  | { kind: 'delete'; id: string }
  | { kind: 'run'; id: string; payload: string | null }
function draftFor(chain: JobChain): Draft {
  return {
    id: chain.id,
    name: chain.name,
    description: chain.description ?? '',
    stages: chain.stages.map((stage) => ({
      jobIds: stage.jobs.map((job) => job.id),
      gate: stage.gate,
      action: stage.action,
    })),
  }
}
function bodyFor(draft: Draft): SaveChainBody {
  return {
    name: draft.name.trim(),
    description: draft.description.trim() || null,
    stages: draft.stages.filter((stage) => stage.jobIds.length > 0 || stage.action !== null),
  }
}
function replaceChain(
  model: JobChainsPageModel | undefined,
  chain: JobChain,
): JobChainsPageModel | undefined {
  if (model === undefined) return model
  return {
    ...model,
    chains: model.chains.some((item) => item.id === chain.id)
      ? model.chains.map((item) => (item.id === chain.id ? chain : item))
      : [...model.chains, chain],
  }
}
function label(stage: ChainStage): string {
  if (stage.action !== null) return `${stage.action.displayName} · ${stage.action.recipient ?? ''}`
  return stage.jobs.map((job) => job.name).join(' + ')
}

export function ChainsPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const options = chainsQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [draft, setDraft] = useState<Draft | null>(null)
  const [tab, setTab] = useState<'details' | 'runs'>('details')
  const [addJob, setAddJob] = useState(data.jobs[0]?.id ?? '')
  const [branches, setBranches] = useState<Record<number, string>>({})
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [message, setMessage] = useState<{
    text: string
    error: boolean
  } | null>(null)
  const [runTarget, setRunTarget] = useState<JobChain | null>(null)
  const [runPayload, setRunPayload] = useState('{}')
  const runs = useQuery({
    ...chainRunsQueryOptions(projectId, draft?.id ?? ''),
    enabled: draft?.id !== null && draft?.id !== undefined && tab === 'runs',
  })
  const mutation = useMutation({
    mutationFn: async (command: Command) => {
      if (command.kind === 'delete') {
        await deleteChain(projectId, command.id, AbortSignal.timeout(30_000))
        return null
      }
      if (command.kind === 'run')
        return runChain(projectId, command.id, command.payload, AbortSignal.timeout(120_000))
      const body = bodyFor(command.draft)
      return command.draft.id === null
        ? createChain(projectId, body, AbortSignal.timeout(30_000))
        : updateChain(projectId, command.draft.id, body, AbortSignal.timeout(30_000))
    },
  })
  const stages = data.chains.reduce((sum, chain) => sum + chain.stages.length, 0)
  const steps = data.chains.reduce(
    (sum, chain) =>
      sum + chain.stages.reduce((stageSum, stage) => stageSum + Math.max(1, stage.jobs.length), 0),
    0,
  )
  const parallel = data.chains.reduce(
    (sum, chain) => sum + chain.stages.filter((stage) => stage.jobs.length > 1).length,
    0,
  )
  function updateStage(index: number, updater: (stage: DraftStage) => DraftStage): void {
    if (draft === null) return
    setDraft({
      ...draft,
      stages: draft.stages.map((stage, stageIndex) =>
        stageIndex === index ? updater(stage) : stage,
      ),
    })
  }
  function moveStage(index: number, delta: number): void {
    if (draft === null) return
    const target = index + delta
    if (target < 0 || target >= draft.stages.length) return
    const next = [...draft.stages]
    const currentStage = next[index]
    const targetStage = next[target]
    if (currentStage === undefined || targetStage === undefined) return
    next[index] = targetStage
    next[target] = currentStage
    setDraft({ ...draft, stages: next })
  }
  async function save(): Promise<void> {
    if (draft === null) return
    if (draft.name.trim() === '') {
      setMessage({ text: 'Name is required.', error: true })
      return
    }
    if (draft.stages.every((stage) => stage.jobIds.length === 0 && stage.action === null)) {
      setMessage({ text: 'Add at least one step.', error: true })
      return
    }
    try {
      const result = await mutation.mutateAsync({ kind: 'save', draft })
      if (result !== null && 'stages' in result)
        client.setQueryData(options.queryKey, (old: JobChainsPageModel | undefined) =>
          replaceChain(old, result),
        )
      setDraft(null)
      setMessage({ text: `Chain '${draft.name.trim()}' saved.`, error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The chain could not be saved.',
        error: true,
      })
    }
  }
  async function remove(id: string): Promise<void> {
    try {
      await mutation.mutateAsync({ kind: 'delete', id })
      client.setQueryData<JobChainsPageModel>(options.queryKey, (old) =>
        old === undefined ? old : { ...old, chains: old.chains.filter((chain) => chain.id !== id) },
      )
      setMessage({ text: 'Chain deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The chain could not be deleted.',
        error: true,
      })
    } finally {
      setConfirmDelete(null)
    }
  }
  async function executeRun(): Promise<void> {
    if (runTarget === null) return
    try {
      const result = await mutation.mutateAsync({
        kind: 'run',
        id: runTarget.id,
        payload: runPayload.trim() || null,
      })
      const status = result !== null && 'status' in result ? result.status : 'started'
      setMessage({ text: `Chain run ${status}.`, error: false })
      setRunTarget(null)
      if (draft?.id === runTarget.id) {
        setTab('runs')
        await runs.refetch()
      }
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The chain could not be run.',
        error: true,
      })
    }
  }
  function open(chain: JobChain): void {
    setDraft(draftFor(chain))
    setTab('details')
  }
  const emailAction = (): ChainAction => ({
    type: 'sendEmail',
    displayName: 'Send email',
    recipient: '',
    recipientName: '',
    subject: '',
    body: '',
    attachmentPath: '',
  })
  const smsAction = (): ChainAction => ({
    type: 'sendSms',
    displayName: 'Send SMS',
    recipient: '',
    recipientName: null,
    subject: null,
    body: '',
    attachmentPath: null,
  })
  return (
    <div className="chains-page-react">
      <title>PlaceContext — Job chains</title>
      <header>
        <div>
          <h1>Job chains</h1>
          <p>
            Connect reusable jobs into ordered pipelines, including parallel stages and delivery
            actions.
          </p>
        </div>
        <div>
          <button
            className="dcbtn"
            onClick={() => {
              void navigate(`/project/${projectId}/jobs`)
            }}
            type="button"
          >
            View jobs
          </button>
          <button
            className="dcbtn primary"
            onClick={() => {
              setDraft({ id: null, name: '', description: '', stages: [] })
              setTab('details')
            }}
            type="button"
          >
            ＋ New chain
          </button>
        </div>
      </header>
      {message === null ? null : (
        <div
          className={message.error ? 'chains-message-react error' : 'chains-message-react'}
          role={message.error ? 'alert' : 'status'}
        >
          {message.text}
        </div>
      )}
      {data.chains.length === 0 ? (
        <section className="dccard chains-empty-react">
          <strong>No job chains yet</strong>
          <span>
            Link reusable jobs into a pipeline, then run the whole workflow with one action.
          </span>
          <button
            className="dcbtn primary"
            onClick={() => {
              setDraft({ id: null, name: '', description: '', stages: [] })
            }}
            type="button"
          >
            Create first chain
          </button>
        </section>
      ) : (
        <>
          <section className="chains-summary-react" aria-label="Job chain summary">
            <div>
              <strong>{data.chains.length}</strong>
              <span>chains</span>
            </div>
            <div>
              <strong>{stages}</strong>
              <span>stages</span>
            </div>
            <div>
              <strong>{steps}</strong>
              <span>job steps</span>
            </div>
            <div>
              <strong>{parallel}</strong>
              <span>parallel</span>
            </div>
          </section>
          <section className="dccard chains-catalog-react">
            <header>
              <strong>Chain catalogue</strong>
              <span>
                {data.chains.length} reusable {data.chains.length === 1 ? 'pipeline' : 'pipelines'}
              </span>
            </header>
            {data.chains.map((chain) => (
              <article
                key={chain.id}
                onClick={() => {
                  open(chain)
                }}
              >
                <span>⇢</span>
                <div>
                  <div>
                    <strong>{chain.name}</strong>
                    <small>{chain.stages.length} stages</small>
                    <small>
                      {chain.stages.reduce((sum, stage) => sum + Math.max(1, stage.jobs.length), 0)}{' '}
                      steps
                    </small>
                  </div>
                  <p>
                    {chain.description ?? 'No description'}
                    <small>updated {chain.updatedAtDisplay}</small>
                  </p>
                  <div className="chain-flow-react">
                    {chain.stages.map((stage, index) => (
                      <span key={`${chain.id}-${String(index)}`}>
                        {index > 0 ? <i>→</i> : null}
                        <b>{label(stage)}</b>
                      </span>
                    ))}
                  </div>
                </div>
                <div
                  onClick={(event) => {
                    event.stopPropagation()
                  }}
                >
                  {confirmDelete === chain.id ? (
                    <>
                      <button
                        className="dcbtn danger"
                        onClick={() => void remove(chain.id)}
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
                        aria-label={`Run ${chain.name}`}
                        onClick={() => {
                          setRunTarget(chain)
                          setRunPayload('{}')
                        }}
                        type="button"
                      >
                        ▶
                      </button>
                      <button
                        aria-label={`Edit ${chain.name}`}
                        onClick={() => {
                          open(chain)
                        }}
                        type="button"
                      >
                        ✎
                      </button>
                      <button
                        aria-label={`Delete ${chain.name}`}
                        onClick={() => {
                          setConfirmDelete(chain.id)
                        }}
                        type="button"
                      >
                        ⋯
                      </button>
                    </>
                  )}
                </div>
              </article>
            ))}
          </section>
        </>
      )}
      {draft === null ? null : (
        <div
          className="chain-drawer-backdrop"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) setDraft(null)
          }}
        >
          <aside className="chain-drawer-react">
            <header>
              <div>
                <strong>{draft.id === null ? 'New chain' : draft.name}</strong>
                <span>Stages run in order; jobs inside one stage run in parallel.</span>
              </div>
              {draft.id === null ? null : (
                <button
                  className="dcbtn primary"
                  onClick={() => {
                    const chain = data.chains.find((item) => item.id === draft.id)
                    if (chain !== undefined) setRunTarget(chain)
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
              </nav>
            )}
            <div className="chain-drawer-body">
              {tab === 'runs' ? (
                runs.isLoading ? (
                  <p>Loading runs…</p>
                ) : runs.data?.length === 0 ? (
                  <p>No runs yet — click Run to start the pipeline.</p>
                ) : (
                  <div className="chain-runs-react">
                    {runs.data?.map((run) => (
                      <article className="dccard" key={run.id}>
                        <header>
                          <strong className={run.status.toLocaleLowerCase()}>{run.status}</strong>
                          <span>{run.startedAtDisplay}</span>
                          <small>
                            {run.steps.filter((step) => step.status === 'Succeeded').length}/
                            {run.steps.length} steps
                          </small>
                          <code>{run.durationDisplay ?? 'running…'}</code>
                        </header>
                        <div>
                          {run.steps.map((step) => (
                            <span className={step.status.toLocaleLowerCase()} key={step.index}>
                              {step.jobName} · {step.status}
                            </span>
                          ))}
                        </div>
                        {run.finalOutput === null ? null : (
                          <details>
                            <summary>Pipeline output</summary>
                            <pre>{run.finalOutput}</pre>
                          </details>
                        )}
                      </article>
                    ))}
                  </div>
                )
              ) : (
                <>
                  <div className="chain-basics-react">
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
                          setDraft({
                            ...draft,
                            description: event.target.value,
                          })
                        }}
                        rows={2}
                        value={draft.description}
                      />
                    </label>
                  </div>
                  <div className="chain-stage-list-react">
                    {draft.stages.map((stage, index) => (
                      <article className="dccard" key={index}>
                        <header>
                          <strong>Stage {index + 1}</strong>
                          <span>
                            {stage.jobIds.length > 1
                              ? 'parallel fan-out'
                              : stage.action === null
                                ? 'job step'
                                : stage.action.displayName}
                          </span>
                          <div>
                            <button
                              disabled={index === 0}
                              onClick={() => {
                                moveStage(index, -1)
                              }}
                              type="button"
                            >
                              ↑
                            </button>
                            <button
                              disabled={index === draft.stages.length - 1}
                              onClick={() => {
                                moveStage(index, 1)
                              }}
                              type="button"
                            >
                              ↓
                            </button>
                            <button
                              onClick={() => {
                                setDraft({
                                  ...draft,
                                  stages: draft.stages.filter(
                                    (_, stageIndex) => stageIndex !== index,
                                  ),
                                })
                              }}
                              type="button"
                            >
                              ×
                            </button>
                          </div>
                        </header>
                        {stage.action === null ? (
                          <>
                            <div className="chain-branches-react">
                              {stage.jobIds.map((id, branchIndex) => (
                                <span key={`${id}-${String(branchIndex)}`}>
                                  {data.jobs.find((job) => job.id === id)?.name ?? 'Deleted job'}
                                  <button
                                    onClick={() => {
                                      updateStage(index, (value) => ({
                                        ...value,
                                        jobIds: value.jobIds.filter(
                                          (_, itemIndex) => itemIndex !== branchIndex,
                                        ),
                                      }))
                                    }}
                                    type="button"
                                  >
                                    ×
                                  </button>
                                </span>
                              ))}
                            </div>
                            <div className="chain-add-branch-react">
                              <select
                                className="dcinput"
                                onChange={(event) => {
                                  setBranches({
                                    ...branches,
                                    [index]: event.target.value,
                                  })
                                }}
                                value={branches[index] ?? data.jobs[0]?.id ?? ''}
                              >
                                {data.jobs.map((job) => (
                                  <option key={job.id} value={job.id}>
                                    {job.name}
                                  </option>
                                ))}
                              </select>
                              <button
                                className="dcbtn"
                                onClick={() => {
                                  const id = branches[index] ?? data.jobs[0]?.id
                                  if (id !== undefined)
                                    updateStage(index, (value) => ({
                                      ...value,
                                      jobIds: [...value.jobIds, id],
                                    }))
                                }}
                                type="button"
                              >
                                ＋ parallel job
                              </button>
                            </div>
                          </>
                        ) : (
                          <div className="chain-action-fields-react">
                            <label>
                              Recipient
                              <input
                                className="dcinput"
                                onChange={(event) => {
                                  updateStage(index, (value) => ({
                                    ...value,
                                    action:
                                      value.action === null
                                        ? null
                                        : {
                                            ...value.action,
                                            recipient: event.target.value,
                                          },
                                  }))
                                }}
                                value={stage.action.recipient ?? ''}
                              />
                            </label>
                            {stage.action.type === 'sendEmail' ? (
                              <>
                                <label>
                                  Subject
                                  <input
                                    className="dcinput"
                                    onChange={(event) => {
                                      updateStage(index, (value) => ({
                                        ...value,
                                        action:
                                          value.action === null
                                            ? null
                                            : {
                                                ...value.action,
                                                subject: event.target.value,
                                              },
                                      }))
                                    }}
                                    value={stage.action.subject ?? ''}
                                  />
                                </label>
                                <label>
                                  Attachment JSON path
                                  <input
                                    className="dcinput"
                                    onChange={(event) => {
                                      updateStage(index, (value) => ({
                                        ...value,
                                        action:
                                          value.action === null
                                            ? null
                                            : {
                                                ...value.action,
                                                attachmentPath: event.target.value,
                                              },
                                      }))
                                    }}
                                    value={stage.action.attachmentPath ?? ''}
                                  />
                                </label>
                              </>
                            ) : null}
                            <label>
                              Body
                              <textarea
                                className="dcinput"
                                onChange={(event) => {
                                  updateStage(index, (value) => ({
                                    ...value,
                                    action:
                                      value.action === null
                                        ? null
                                        : {
                                            ...value.action,
                                            body: event.target.value,
                                          },
                                  }))
                                }}
                                rows={3}
                                value={stage.action.body ?? ''}
                              />
                            </label>
                          </div>
                        )}
                        <div className="chain-gate-react">
                          <select
                            className="dcinput"
                            onChange={(event) => {
                              const type = event.target.value
                              updateStage(index, (value) => ({
                                ...value,
                                gate:
                                  type === ''
                                    ? null
                                    : type === 'wait'
                                      ? {
                                          type,
                                          durationSeconds: 30,
                                          expression: null,
                                        }
                                      : {
                                          type,
                                          durationSeconds: null,
                                          expression: 'exists:data',
                                        },
                              }))
                            }}
                            value={stage.gate?.type ?? ''}
                          >
                            <option value="">No gate</option>
                            <option value="wait">Wait</option>
                            <option value="condition">Condition</option>
                          </select>
                          {stage.gate?.type === 'wait' ? (
                            <input
                              aria-label={`Stage ${String(index + 1)} wait seconds`}
                              className="dcinput"
                              min={0}
                              onChange={(event) => {
                                updateStage(index, (value) => ({
                                  ...value,
                                  gate:
                                    value.gate === null
                                      ? null
                                      : {
                                          ...value.gate,
                                          durationSeconds: event.target.valueAsNumber,
                                        },
                                }))
                              }}
                              type="number"
                              value={stage.gate.durationSeconds ?? 30}
                            />
                          ) : stage.gate?.type === 'condition' ? (
                            <input
                              aria-label={`Stage ${String(index + 1)} condition`}
                              className="dcinput"
                              onChange={(event) => {
                                updateStage(index, (value) => ({
                                  ...value,
                                  gate:
                                    value.gate === null
                                      ? null
                                      : {
                                          ...value.gate,
                                          expression: event.target.value,
                                        },
                                }))
                              }}
                              value={stage.gate.expression ?? ''}
                            />
                          ) : null}
                        </div>
                      </article>
                    ))}
                  </div>
                  <div className="chain-add-stage-react">
                    <select
                      className="dcinput"
                      onChange={(event) => {
                        setAddJob(event.target.value)
                      }}
                      value={addJob}
                    >
                      {data.jobs.map((job) => (
                        <option key={job.id} value={job.id}>
                          {job.name}
                        </option>
                      ))}
                    </select>
                    <button
                      className="dcbtn"
                      disabled={addJob === ''}
                      onClick={() => {
                        setDraft({
                          ...draft,
                          stages: [...draft.stages, { jobIds: [addJob], gate: null, action: null }],
                        })
                      }}
                      type="button"
                    >
                      ＋ Job stage
                    </button>
                    {data.canSendEmail ? (
                      <button
                        className="dcbtn"
                        onClick={() => {
                          setDraft({
                            ...draft,
                            stages: [
                              ...draft.stages,
                              { jobIds: [], gate: null, action: emailAction() },
                            ],
                          })
                        }}
                        type="button"
                      >
                        ＋ Email action
                      </button>
                    ) : null}
                    {data.canSendSms ? (
                      <button
                        className="dcbtn"
                        onClick={() => {
                          setDraft({
                            ...draft,
                            stages: [
                              ...draft.stages,
                              { jobIds: [], gate: null, action: smsAction() },
                            ],
                          })
                        }}
                        type="button"
                      >
                        ＋ SMS action
                      </button>
                    ) : null}
                  </div>
                </>
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
                  {mutation.isPending ? 'Saving…' : 'Save chain'}
                </button>
              </footer>
            ) : null}
          </aside>
        </div>
      )}
      {runTarget === null ? null : (
        <div className="chain-run-modal">
          <section className="dccard" aria-label={`Run ${runTarget.name}`} role="dialog">
            <header>
              <strong>Run {runTarget.name}</strong>
              <button
                aria-label="Close"
                onClick={() => {
                  setRunTarget(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <label>
              Initial payload
              <textarea
                className="dcinput"
                onChange={(event) => {
                  setRunPayload(event.target.value)
                }}
                rows={7}
                spellCheck={false}
                value={runPayload}
              />
            </label>
            <footer>
              <button
                className="dcbtn"
                onClick={() => {
                  setRunTarget(null)
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
                {mutation.isPending ? 'Running…' : 'Run chain'}
              </button>
            </footer>
          </section>
        </div>
      )}
    </div>
  )
}
