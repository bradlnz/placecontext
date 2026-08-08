import { useMutation, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useId, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { runJobTestCode, saveJobTestCode } from '../../api/job-tests-api'
import { jobTestCodeQueryOptions } from '../../api/job-tests-query'
import type { JobTestBlock, JobTestCodeFile, UpdateJobTestCodeBody } from '../../model/job-tests'
import { languageForPath, loadHostMonaco } from '../../../../shared/editor/load-host-monaco'

function formatDuration(value: number): string {
  return value < 1000 ? `${String(value)} ms` : `${(value / 1000).toFixed(1)} s`
}
function prettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value) as unknown, null, 2)
  } catch {
    return value
  }
}
function normalizePath(value: string): string {
  return value
    .trim()
    .replaceAll('\\', '/')
    .replace(/^\/+/, '')
    .replaceAll(/\/{2,}/g, '/')
}

export function TestCodeEditorPage() {
  const { projectId = '', testId = '' } = useParams<{
    projectId: string
    testId: string
  }>()
  const navigate = useNavigate()
  const { data } = useSuspenseQuery(jobTestCodeQueryOptions(projectId, testId))
  const generatedId = useId()
  const editorId = `pctest-react-${generatedId.replaceAll(':', '')}`
  const runtime = data.runtimes.find((item) => item.id === data.test.runtimeId) ?? data.runtimes[0]
  const initialFiles =
    data.test.codeFiles.length > 0 ? data.test.codeFiles : (runtime?.starterFiles ?? [])
  const [test, setTest] = useState<JobTestBlock>(data.test)
  const [runtimeId, setRuntimeId] = useState(runtime?.id ?? 'python')
  const [files, setFiles] = useState<JobTestCodeFile[]>(initialFiles)
  const [active, setActive] = useState(
    Math.max(
      0,
      initialFiles.findIndex((file) => file.path === data.test.entrypoint),
    ),
  )
  const [entrypoint, setEntrypoint] = useState(
    data.test.entrypoint ?? runtime?.entrypoint ?? initialFiles[0]?.path ?? null,
  )
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [basicEditor, setBasicEditor] = useState(false)
  const [addingFile, setAddingFile] = useState(false)
  const [newFileName, setNewFileName] = useState('')
  const [panelOpen, setPanelOpen] = useState(true)
  const initialEditorFile = useRef(
    initialFiles[
      Math.max(
        0,
        initialFiles.findIndex((file) => file.path === data.test.entrypoint),
      )
    ],
  )
  const mutation = useMutation({
    mutationFn: async (command: { run: boolean; body: UpdateJobTestCodeBody }) =>
      command.run
        ? runJobTestCode(projectId, testId, command.body, AbortSignal.timeout(60_000))
        : saveJobTestCode(projectId, testId, command.body, AbortSignal.timeout(30_000)),
  })

  useEffect(() => {
    let cancelled = false
    const first = initialEditorFile.current
    if (first === undefined) return
    void loadHostMonaco()
      .then(async (editor) => {
        const rich = await editor.init(
          editorId,
          first.content,
          languageForPath(first.path),
          'vs-dark',
          first.path,
        )
        if (!cancelled) setBasicEditor(!rich)
      })
      .catch(() => {
        if (!cancelled) setBasicEditor(true)
      })
    return () => {
      cancelled = true
      window.pcmonaco?.destroy(editorId)
    }
  }, [editorId])

  function snapshot(): JobTestCodeFile[] {
    const current = window.pcmonaco?.getValue(editorId)
    return files.map((file, index) =>
      index === active && current !== null && current !== undefined
        ? { ...file, content: current }
        : file,
    )
  }
  function switchFile(index: number): void {
    if (index < 0 || index >= files.length || index === active) return
    const next = snapshot()
    setFiles(next)
    setActive(index)
    const file = next[index]
    if (file !== undefined)
      window.pcmonaco?.openFile(editorId, file.path, file.content, languageForPath(file.path))
  }
  function addFile(): void {
    const path = normalizePath(newFileName)
    if (path === '') {
      setAddingFile(false)
      return
    }
    if (files.some((file) => file.path === path)) {
      setError(`'${path}' already exists.`)
      return
    }
    const next = [...snapshot(), { path, content: '' }]
    setFiles(next)
    setActive(next.length - 1)
    setAddingFile(false)
    setNewFileName('')
    window.pcmonaco?.openFile(editorId, path, '', languageForPath(path))
  }
  function deleteFile(index: number): void {
    if (files.length <= 1 || index < 0 || index >= files.length) return
    const current = snapshot()
    const removed = current[index]
    if (removed === undefined) return
    const next = current.filter((_, fileIndex) => fileIndex !== index)
    window.pcmonaco?.closeFile(editorId, removed.path)
    const nextIndex = Math.min(active > index ? active - 1 : active, next.length - 1)
    setFiles(next)
    setActive(nextIndex)
    if (entrypoint === removed.path) setEntrypoint(next[0]?.path ?? null)
    const file = next[nextIndex]
    if (file !== undefined)
      window.pcmonaco?.openFile(editorId, file.path, file.content, languageForPath(file.path))
  }
  function resetStarter(): void {
    const selected = data.runtimes.find((item) => item.id === runtimeId)
    if (selected === undefined) return
    for (const file of files) window.pcmonaco?.closeFile(editorId, file.path)
    const next = selected.starterFiles.map((file) => ({ ...file }))
    setFiles(next)
    setActive(0)
    setEntrypoint(selected.entrypoint)
    const first = next[0]
    if (first !== undefined)
      window.pcmonaco?.openFile(editorId, first.path, first.content, languageForPath(first.path))
    setMessage(`${selected.label} starter loaded. Save to keep it.`)
    setError(null)
  }
  async function execute(run: boolean): Promise<void> {
    const next = snapshot()
    if (next.length === 0) return
    const selectedEntry =
      entrypoint === null || entrypoint.trim() === '' ? (next[0]?.path ?? null) : entrypoint
    setFiles(next)
    setError(null)
    setMessage(null)
    try {
      const result = await mutation.mutateAsync({
        run,
        body: { runtimeId, entrypoint: selectedEntry, codeFiles: next },
      })
      setTest(result)
      setEntrypoint(result.entrypoint ?? selectedEntry)
      setPanelOpen(true)
      setMessage(run ? result.lastStatus : 'Test code saved.')
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : 'The test code could not be saved.')
    }
  }
  const runtimeOption = data.runtimes.find((item) => item.id === runtimeId)
  const lastResult = test.lastStatus === 'NotRun' ? null : test

  return (
    <div className="test-code-page-react">
      <title>PlaceContext — {test.name}</title>
      <header className="test-code-toolbar">
        <button
          aria-label="Back to Tests"
          className="dcbtn"
          onClick={() => {
            void navigate(`/project/${projectId}/tests`)
          }}
          type="button"
        >
          ←
        </button>
        <div>
          <strong>{test.name}</strong>
          <span>{test.jobName}</span>
        </div>
        <select
          aria-label="Runtime"
          onChange={(event) => {
            setRuntimeId(event.target.value)
            setMessage('Runtime changed. Use Starter to replace the files with a matching example.')
          }}
          value={runtimeId}
        >
          {data.runtimes.map((item) => (
            <option key={item.id} value={item.id}>
              {item.label} · {item.frameworkLabel}
            </option>
          ))}
        </select>
        {basicEditor ? <span className="editor-warning-react">basic editor</span> : null}
        <i />
        {message === null ? null : <span>{message}</span>}
        <b title="No network, Vault secrets, external services, or production Job side effects">
          Isolated
        </b>
        <button
          className="dcbtn"
          disabled={mutation.isPending}
          onClick={resetStarter}
          type="button"
        >
          Starter
        </button>
        <button
          className="dcbtn"
          disabled={mutation.isPending}
          onClick={() => void execute(true)}
          type="button"
        >
          {mutation.isPending && mutation.variables.run ? 'Running…' : '▶ Run block'}
        </button>
        <button
          className="dcbtn primary"
          disabled={mutation.isPending}
          onClick={() => void execute(false)}
          type="button"
        >
          {mutation.isPending && !mutation.variables.run ? 'Saving…' : 'Save code'}
        </button>
      </header>
      {error === null ? null : (
        <div className="test-code-error" role="alert">
          {error}
        </div>
      )}
      <div className="test-contract-react">
        <strong>{runtimeOption?.label ?? runtimeId}</strong>
        <code>
          {'{ "input": …, "run": { "status": "Succeeded", "output": …, "shards": […] } }'}
        </code>
        <span>
          Every framework method runs against the declared mock scenario. Inline Job source is
          available under <code>job/</code> for imports and mocks; the selected Job is never
          executed.
        </span>
      </div>
      <main className="test-code-workspace">
        <aside className="test-code-tree">
          <header>
            <span>Test methods</span>
            <b>{test.methodResults.length}</b>
          </header>
          <div className="test-method-list">
            {test.methodResults.length === 0 ? (
              <span>Add a framework test method, then save or run the block.</span>
            ) : (
              test.methodResults.map((method) => (
                <div
                  className={method.status.toLocaleLowerCase()}
                  key={method.name}
                  title={method.message ?? ''}
                >
                  <b>
                    {method.status === 'Passed'
                      ? '✓'
                      : method.status === 'Failed'
                        ? '×'
                        : method.status === 'Skipped'
                          ? '–'
                          : '○'}
                  </b>
                  <span>{method.name}</span>
                  {method.durationMs === null ? null : (
                    <small>{formatDuration(method.durationMs)}</small>
                  )}
                </div>
              ))
            )}
          </div>
          <header>
            <span>Test files</span>
            <button
              aria-label="Add file"
              onClick={() => {
                setAddingFile(true)
              }}
              type="button"
            >
              ＋
            </button>
          </header>
          {addingFile ? (
            <div className="test-add-file">
              <input
                autoFocus
                className="dcinput"
                onChange={(event) => {
                  setNewFileName(event.target.value)
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') addFile()
                  else if (event.key === 'Escape') setAddingFile(false)
                }}
                placeholder="path/name.py"
                value={newFileName}
              />
              <button onClick={addFile} type="button">
                ✓
              </button>
            </div>
          ) : null}
          <div className="test-file-list">
            {files.map((file, index) => (
              <div
                className={index === active ? 'active' : ''}
                key={file.path}
                onClick={() => {
                  switchFile(index)
                }}
                role="button"
                tabIndex={0}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') switchFile(index)
                }}
              >
                <span title={file.path}>{file.path}</span>
                {file.path === entrypoint ? (
                  <b title="Entrypoint">★</b>
                ) : (
                  <button
                    aria-label={`Set ${file.path} as entrypoint`}
                    onClick={(event) => {
                      event.stopPropagation()
                      setEntrypoint(file.path)
                    }}
                    type="button"
                  >
                    ☆
                  </button>
                )}
                {files.length > 1 ? (
                  <button
                    aria-label={`Delete ${file.path}`}
                    onClick={(event) => {
                      event.stopPropagation()
                      deleteFile(index)
                    }}
                    type="button"
                  >
                    ×
                  </button>
                ) : null}
              </div>
            ))}
          </div>
          <footer>
            <span>Entrypoint</span>
            <code>{entrypoint ?? runtimeOption?.entrypoint}</code>
            <small>
              Dependency manifests are supported and use the same warm-cache path as Jobs.
            </small>
          </footer>
        </aside>
        <section className="test-editor-pane">
          <div className="test-editor-host" id={editorId} />
          <div className={panelOpen ? 'test-result-panel open' : 'test-result-panel'}>
            <button
              onClick={() => {
                setPanelOpen((value) => !value)
              }}
              type="button"
            >
              <span>Block result</span>
              {lastResult === null ? null : (
                <>
                  <b className={lastResult.lastStatus.toLocaleLowerCase()}>
                    {lastResult.lastStatus}
                  </b>
                  {lastResult.lastDurationMs === null ? null : (
                    <small>{formatDuration(lastResult.lastDurationMs)}</small>
                  )}
                </>
              )}
              <i />
              <span>{panelOpen ? '▾' : '▴'}</span>
            </button>
            {panelOpen ? (
              <div>
                {lastResult === null ? (
                  <span>
                    Run the block to execute every discovered framework test method against the
                    declared mock scenario.
                  </span>
                ) : (
                  <>
                    <strong className={lastResult.lastStatus.toLocaleLowerCase()}>
                      {lastResult.lastMessage}
                    </strong>
                    {lastResult.lastActualOutput === null ? null : (
                      <details>
                        <summary>Job output</summary>
                        <pre>{prettyJson(lastResult.lastActualOutput)}</pre>
                      </details>
                    )}
                  </>
                )}
              </div>
            ) : null}
          </div>
        </section>
      </main>
    </div>
  )
}
