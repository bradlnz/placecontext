import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { createJobTest, deleteJobTest, runJobTest, updateJobTest } from '../../api/job-tests-api'
import { jobTestsQueryOptions } from '../../api/job-tests-query'
import type {
  JobTestAssertion,
  JobTestBlock,
  JobTestsPageModel,
  SaveJobTestBlockBody,
} from '../../model/job-tests'

const DEFAULT_SCENARIO =
  '{\n  "input": {"customerId":"example"},\n  "run": {"status": "Succeeded", "output": {"status":"active"}, "shards": []}\n}'
const assertionLabels: Record<JobTestAssertion, string> = {
  Succeeds: 'Run succeeds',
  OutputEquals: 'Output equals',
  OutputContains: 'Output contains',
  JsonSubset: 'JSON subset',
}
type EditState = SaveJobTestBlockBody & { id: string | null }
type Command =
  { kind: 'save'; edit: EditState } | { kind: 'run'; id: string } | { kind: 'delete'; id: string }

function duration(value: number): string {
  return value < 1000 ? `${String(value)} ms` : `${(value / 1000).toFixed(1)} s`
}
function replaceTest(
  model: JobTestsPageModel | undefined,
  test: JobTestBlock,
): JobTestsPageModel | undefined {
  if (model === undefined) return model
  const exists = model.tests.some((item) => item.id === test.id)
  return {
    ...model,
    tests: exists
      ? model.tests.map((item) => (item.id === test.id ? test : item))
      : [...model.tests, test],
  }
}

export function TestsPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const options = jobTestsQueryOptions(projectId)
  const { data } = useSuspenseQuery(options)
  const client = useQueryClient()
  const [editing, setEditing] = useState<EditState | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [running, setRunning] = useState<Set<string>>(new Set())
  const [message, setMessage] = useState<{
    text: string
    error: boolean
  } | null>(null)
  const mutation = useMutation({
    mutationFn: async (command: Command) => {
      const signal = AbortSignal.timeout(30_000)
      if (command.kind === 'run') return runJobTest(projectId, command.id, signal)
      if (command.kind === 'delete') {
        await deleteJobTest(projectId, command.id, signal)
        return null
      }
      const body: SaveJobTestBlockBody = command.edit
      return command.edit.id === null
        ? createJobTest(projectId, body, signal)
        : updateJobTest(projectId, command.edit.id, body, signal)
    },
  })
  const methods = data.tests.flatMap((test) => test.methodResults)
  const passed = methods.filter((method) => method.status === 'Passed').length
  const failed = methods.filter((method) => method.status === 'Failed').length
  const notRun = Math.max(0, methods.length - passed - failed)
  const passPercent = methods.length === 0 ? 0 : Math.round((passed * 100) / methods.length)
  const suites = new Map<string, JobTestBlock[]>()
  for (const test of data.tests)
    suites.set(test.jobName, [...(suites.get(test.jobName) ?? []), test])

  function newTest(): void {
    setEditing({
      id: null,
      jobId: data.jobs[0]?.id ?? '',
      name: '',
      inputPayload: DEFAULT_SCENARIO,
      assertionType: 'Succeeds',
      expectedValue: '',
      enabled: true,
    })
  }
  function editTest(test: JobTestBlock): void {
    setEditing({
      id: test.id,
      jobId: test.jobId,
      name: test.name,
      inputPayload: test.inputPayload ?? '',
      assertionType: test.assertionType,
      expectedValue: test.expectedValue ?? '',
      enabled: test.enabled,
    })
  }
  async function save(): Promise<void> {
    if (editing === null) return
    if (editing.jobId === '' || editing.name.trim() === '') {
      setMessage({ text: 'Choose a Job and enter a block name.', error: true })
      return
    }
    if (editing.assertionType !== 'Succeeds' && editing.expectedValue.trim() === '') {
      setMessage({
        text: 'Enter the expected value for this assertion.',
        error: true,
      })
      return
    }
    setMessage(null)
    try {
      const result = await mutation.mutateAsync({
        kind: 'save',
        edit: { ...editing, name: editing.name.trim() },
      })
      if (result !== null)
        client.setQueryData(options.queryKey, (old: JobTestsPageModel | undefined) =>
          replaceTest(old, result),
        )
      setEditing(null)
      setMessage({ text: 'Saved block.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The block could not be saved.',
        error: true,
      })
    }
  }
  async function run(id: string): Promise<JobTestBlock | null> {
    setConfirmDelete(null)
    setRunning((value) => new Set(value).add(id))
    try {
      const result = await mutation.mutateAsync({ kind: 'run', id })
      if (result !== null)
        client.setQueryData(options.queryKey, (old: JobTestsPageModel | undefined) =>
          replaceTest(old, result),
        )
      return result
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The block could not be run.',
        error: true,
      })
      return null
    } finally {
      setRunning((value) => {
        const next = new Set(value)
        next.delete(id)
        return next
      })
    }
  }
  async function runAll(): Promise<void> {
    let failureCount = 0
    let methodCount = 0
    for (const test of data.tests.filter((item) => item.enabled)) {
      const result = await run(test.id)
      if (result !== null) {
        methodCount += result.methodResults.length
        failureCount += result.methodResults.filter((method) => method.status === 'Failed').length
      }
    }
    setMessage({
      text:
        failureCount === 0
          ? `All ${String(methodCount)} enabled test methods passed.`
          : `${String(failureCount)} test methods failed.`,
      error: failureCount > 0,
    })
  }
  async function remove(id: string): Promise<void> {
    try {
      await mutation.mutateAsync({ kind: 'delete', id })
      client.setQueryData<JobTestsPageModel>(options.queryKey, (old) =>
        old === undefined ? old : { ...old, tests: old.tests.filter((test) => test.id !== id) },
      )
      setMessage({ text: 'Test block deleted.', error: false })
    } catch (error: unknown) {
      setMessage({
        text: error instanceof Error ? error.message : 'The block could not be deleted.',
        error: true,
      })
    } finally {
      setConfirmDelete(null)
    }
  }

  return (
    <div className="tests-page-react">
      <title>PlaceContext — Tests</title>
      <header className="tests-head-react">
        <div>
          <h1>Tests</h1>
          <p>
            Verify Job logic against declared mock scenarios with no live Job execution or
            production side effects.
          </p>
        </div>
        <div>
          <button
            className="dcbtn"
            disabled={running.size > 0 || data.tests.every((test) => !test.enabled)}
            onClick={() => void runAll()}
            type="button"
          >
            {running.size > 0 ? 'Running…' : 'Run all'}
          </button>
          <button
            className="dcbtn primary"
            disabled={data.jobs.length === 0}
            onClick={newTest}
            type="button"
          >
            ＋ New block
          </button>
        </div>
      </header>
      {message === null ? null : (
        <div
          className={message.error ? 'test-message-react error' : 'test-message-react'}
          role={message.error ? 'alert' : 'status'}
        >
          {message.text}
        </div>
      )}
      {data.jobs.length === 0 ? (
        <section className="dccard tests-empty-react">
          <strong>Create a Job before adding tests</strong>
          <span>Tests use declared mock scenarios and never execute the selected Job.</span>
          <button
            className="dcbtn primary"
            onClick={() => {
              void navigate(`/project/${projectId}/jobs`)
            }}
            type="button"
          >
            Open Jobs
          </button>
        </section>
      ) : data.tests.length === 0 ? (
        <section className="dccard tests-empty-react">
          <strong>No test blocks yet</strong>
          <span>
            Add a framework-backed block with multiple test methods to start verifying Job code.
          </span>
          <button className="dcbtn primary" onClick={newTest} type="button">
            Create first block
          </button>
        </section>
      ) : (
        <>
          <section aria-label="Test summary" className="test-summary-react">
            <div>
              <strong>{methods.length}</strong>
              <span>methods</span>
            </div>
            <div className="passed">
              <strong>{passed}</strong>
              <span>passed</span>
            </div>
            <div className="failed">
              <strong>{failed}</strong>
              <span>failed</span>
            </div>
            <div>
              <strong>{notRun}</strong>
              <span>not run</span>
            </div>
            <i>
              <span style={{ width: `${String(passPercent)}%` }} />
            </i>
          </section>
          <div className="test-suites-react">
            {[...suites.entries()]
              .sort(([a], [b]) => a.localeCompare(b))
              .map(([jobName, tests]) => {
                const suiteMethods = tests.flatMap((test) => test.methodResults)
                const suitePassed = suiteMethods.filter(
                  (method) => method.status === 'Passed',
                ).length
                return (
                  <section className="dccard test-suite-react" key={jobName}>
                    <header>
                      <div>
                        <strong>{jobName}</strong>
                        <span>
                          {tests.length} {tests.length === 1 ? 'block' : 'blocks'} ·{' '}
                          {suiteMethods.length} methods
                        </span>
                      </div>
                      <span>
                        {suitePassed} / {suiteMethods.length} passing
                      </span>
                    </header>
                    {tests.map((test) => {
                      const isRunning = running.has(test.id)
                      const state = isRunning ? 'running' : test.lastStatus.toLocaleLowerCase()
                      return (
                        <article className={`test-row-react ${state}`} key={test.id}>
                          <span className="test-status-react">
                            {isRunning
                              ? '◌'
                              : test.lastStatus === 'Passed'
                                ? '✓'
                                : test.lastStatus === 'Failed'
                                  ? '×'
                                  : '○'}
                          </span>
                          <div>
                            <div className="test-title-react">
                              <strong>{test.name}</strong>
                              {test.enabled ? null : <span>disabled</span>}
                              {test.codeFiles.length === 0 ? (
                                <span>{assertionLabels[test.assertionType]}</span>
                              ) : null}
                              <span>
                                {test.runtimeLabel} · {test.methodResults.length} methods
                              </span>
                            </div>
                            <p>
                              {isRunning
                                ? 'Running framework tests against the mock scenario…'
                                : (test.lastMessage ?? 'Not run yet')}
                              {test.lastDurationMs === null ? null : (
                                <small>{duration(test.lastDurationMs)}</small>
                              )}
                            </p>
                            {!isRunning &&
                            test.lastStatus === 'Failed' &&
                            test.lastActualOutput !== null ? (
                              <details>
                                <summary>Actual output</summary>
                                <pre>{test.lastActualOutput}</pre>
                              </details>
                            ) : null}
                          </div>
                          <div className="test-actions-react">
                            {confirmDelete === test.id ? (
                              <>
                                <button
                                  className="dcbtn danger"
                                  onClick={() => void remove(test.id)}
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
                                  aria-label={`Run ${test.name}`}
                                  disabled={isRunning || !test.enabled}
                                  onClick={() => void run(test.id)}
                                  type="button"
                                >
                                  ▶
                                </button>
                                <button
                                  aria-label={`Edit ${test.name}`}
                                  onClick={() => {
                                    editTest(test)
                                  }}
                                  type="button"
                                >
                                  ✎
                                </button>
                                <button
                                  aria-label={`Open code editor for ${test.name}`}
                                  onClick={() => {
                                    void navigate(`/project/${projectId}/tests/${test.id}`)
                                  }}
                                  type="button"
                                >
                                  &lt;/&gt;
                                </button>
                                <button
                                  aria-label={`Delete ${test.name}`}
                                  onClick={() => {
                                    setConfirmDelete(test.id)
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
                )
              })}
          </div>
        </>
      )}
      {editing === null ? null : (
        <div
          className="test-modal-backdrop"
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) setEditing(null)
          }}
        >
          <aside
            aria-labelledby="test-editor-title"
            aria-modal="true"
            className="test-modal-react"
            role="dialog"
          >
            <header>
              <div>
                <span>JOB TEST BLOCK</span>
                <h2 id="test-editor-title">{editing.id === null ? 'New block' : 'Edit block'}</h2>
              </div>
              <button
                aria-label="Close"
                onClick={() => {
                  setEditing(null)
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <div className="test-modal-body">
              <label>
                Job
                <select
                  className="dcinput"
                  onChange={(event) => {
                    setEditing({ ...editing, jobId: event.target.value })
                  }}
                  value={editing.jobId}
                >
                  {data.jobs.map((job) => (
                    <option key={job.id} value={job.id}>
                      {job.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Block name
                <input
                  className="dcinput"
                  onChange={(event) => {
                    setEditing({ ...editing, name: event.target.value })
                  }}
                  placeholder="customer output contract"
                  value={editing.name}
                />
              </label>
              <label>
                Mock scenario JSON
                <textarea
                  className="dcinput"
                  onChange={(event) => {
                    setEditing({
                      ...editing,
                      inputPayload: event.target.value,
                    })
                  }}
                  rows={7}
                  spellCheck={false}
                  value={editing.inputPayload}
                />
                <small>
                  Defines input and the mocked status, output, and shard results. Never executes the
                  selected Job.
                </small>
              </label>
              <label>
                Assertion
                <select
                  className="dcinput"
                  onChange={(event) => {
                    setEditing({
                      ...editing,
                      assertionType: event.target.value as JobTestAssertion,
                    })
                  }}
                  value={editing.assertionType}
                >
                  {(Object.keys(assertionLabels) as JobTestAssertion[]).map((assertion) => (
                    <option key={assertion} value={assertion}>
                      {assertionLabels[assertion]}
                    </option>
                  ))}
                </select>
              </label>
              {editing.assertionType === 'Succeeds' ? null : (
                <label>
                  Expected value
                  <textarea
                    className="dcinput"
                    onChange={(event) => {
                      setEditing({
                        ...editing,
                        expectedValue: event.target.value,
                      })
                    }}
                    placeholder={
                      editing.assertionType === 'JsonSubset' ? '{"status":"ok"}' : 'Expected output'
                    }
                    rows={6}
                    value={editing.expectedValue}
                  />
                  <small>
                    {editing.assertionType === 'JsonSubset'
                      ? 'Object properties may be a subset; arrays are compared by position.'
                      : editing.assertionType === 'OutputContains'
                        ? 'Passes when the primary output contains this exact text.'
                        : 'Passes when the trimmed primary output is exactly this value.'}
                  </small>
                </label>
              )}
              <label className="test-enabled-react">
                <input
                  checked={editing.enabled}
                  onChange={(event) => {
                    setEditing({ ...editing, enabled: event.target.checked })
                  }}
                  type="checkbox"
                />
                <span>
                  <strong>Enabled</strong>
                  <small>Included when Run all is selected.</small>
                </span>
              </label>
              <div className="test-code-callout">
                <div>
                  <strong>Framework test methods</strong>
                  <small>
                    Add multiple methods using pytest, Node test, Go testing, or Ruby Minitest.
                  </small>
                </div>
                {editing.id === null ? (
                  <small>
                    Save this block first, then add its test methods in the code editor.
                  </small>
                ) : (
                  <button
                    className="dcbtn"
                    onClick={() => {
                      void navigate(`/project/${projectId}/tests/${editing.id ?? ''}`)
                    }}
                    type="button"
                  >
                    &lt;/&gt; Open code editor
                  </button>
                )}
              </div>
            </div>
            <footer>
              <button
                className="dcbtn"
                onClick={() => {
                  setEditing(null)
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
                {mutation.isPending ? 'Saving…' : 'Save block'}
              </button>
            </footer>
          </aside>
        </div>
      )}
    </div>
  )
}
