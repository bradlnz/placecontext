import { useMutation, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useId, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { runJobCode, saveJobCode } from '../../api/jobs-api'
import { jobCodeQueryOptions } from '../../api/jobs-query'
import type { Job, JobCodeFile, JobRunDetail } from '../../model/jobs'
import { languageForPath, loadHostMonaco } from '../../../../shared/editor/load-host-monaco'

function normalizePath(value: string): string {
  return value
    .trim()
    .replaceAll('\\', '/')
    .replace(/^\/+/, '')
    .replaceAll(/\/{2,}/g, '/')
}
function pretty(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value) as unknown, null, 2)
  } catch {
    return value
  }
}

export function JobCodeEditorPage() {
  const { projectId = '', jobId = '' } = useParams<{
    projectId: string
    jobId: string
  }>()
  const navigate = useNavigate()
  const { data } = useSuspenseQuery(jobCodeQueryOptions(projectId, jobId))
  const generatedId = useId()
  const editorId = `pcjob-react-${generatedId.replaceAll(':', '')}`
  const initialFiles =
    data.job.mapFiles.length > 0
      ? data.job.mapFiles
      : data.job.mapSource === null
        ? []
        : [
            {
              path: data.job.mapEntrypoint ?? 'main',
              content: data.job.mapSource,
            },
          ]
  const initialIndex = Math.max(
    0,
    initialFiles.findIndex((file) => file.path === data.job.mapEntrypoint),
  )
  const [job, setJob] = useState<Job>(data.job)
  const [runtimeId] = useState(data.job.mapRuntimeId ?? 'python')
  const [files, setFiles] = useState<JobCodeFile[]>(initialFiles)
  const [active, setActive] = useState(initialIndex)
  const [entrypoint, setEntrypoint] = useState(data.job.mapEntrypoint)
  const [lastRun, setLastRun] = useState<JobRunDetail | null>(null)
  const [panelOpen, setPanelOpen] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [basic, setBasic] = useState(false)
  const [adding, setAdding] = useState(false)
  const [newPath, setNewPath] = useState('')
  const [renaming, setRenaming] = useState<number | null>(null)
  const [renamePath, setRenamePath] = useState('')
  const firstFile = useRef(initialFiles[initialIndex])
  const saveMutation = useMutation({
    mutationFn: async (payload: { entrypoint: string | null; files: JobCodeFile[] }) =>
      saveJobCode(
        projectId,
        jobId,
        runtimeId,
        payload.entrypoint,
        payload.files,
        AbortSignal.timeout(30_000),
      ),
  })
  const runMutation = useMutation({
    mutationFn: async (payload: { entrypoint: string | null; files: JobCodeFile[] }) =>
      runJobCode(
        projectId,
        jobId,
        runtimeId,
        payload.entrypoint,
        payload.files,
        AbortSignal.timeout(120_000),
      ),
  })
  const busy = saveMutation.isPending || runMutation.isPending
  useEffect(() => {
    let cancelled = false
    const file = firstFile.current
    if (file === undefined) return
    void loadHostMonaco()
      .then(async (editor) => {
        const rich = await editor.init(
          editorId,
          file.content,
          languageForPath(file.path),
          'vs-dark',
          file.path,
        )
        if (!cancelled) setBasic(!rich)
      })
      .catch(() => {
        if (!cancelled) setBasic(true)
      })
    return () => {
      cancelled = true
      window.pcmonaco?.destroy(editorId)
    }
  }, [editorId])
  function snapshot(): JobCodeFile[] {
    const content = window.pcmonaco?.getValue(editorId)
    return files.map((file, index) =>
      index === active && content !== null && content !== undefined ? { ...file, content } : file,
    )
  }
  function open(index: number): void {
    if (index === active || index < 0 || index >= files.length) return
    const next = snapshot()
    setFiles(next)
    setActive(index)
    const file = next[index]
    if (file !== undefined)
      window.pcmonaco?.openFile(editorId, file.path, file.content, languageForPath(file.path))
  }
  function add(): void {
    const path = normalizePath(newPath)
    if (path === '') {
      setAdding(false)
      return
    }
    if (files.some((file) => file.path === path)) {
      setError(`'${path}' already exists.`)
      return
    }
    const next = [...snapshot(), { path, content: '' }]
    setFiles(next)
    setActive(next.length - 1)
    setAdding(false)
    setNewPath('')
    window.pcmonaco?.openFile(editorId, path, '', languageForPath(path))
  }
  function remove(index: number): void {
    if (files.length <= 1) return
    const next = snapshot()
    const removed = next[index]
    if (removed === undefined) return
    window.pcmonaco?.closeFile(editorId, removed.path)
    const remaining = next.filter((_, fileIndex) => fileIndex !== index)
    const nextIndex = Math.min(active > index ? active - 1 : active, remaining.length - 1)
    setFiles(remaining)
    setActive(nextIndex)
    if (entrypoint === removed.path)
      setEntrypoint(remaining.length === 1 ? null : (remaining[0]?.path ?? null))
    const file = remaining[nextIndex]
    if (file !== undefined)
      window.pcmonaco?.openFile(editorId, file.path, file.content, languageForPath(file.path))
  }
  function rename(index: number): void {
    const path = normalizePath(renamePath)
    const current = files[index]
    if (current === undefined || path === '' || path === current.path) {
      setRenaming(null)
      return
    }
    if (files.some((file) => file.path === path)) {
      setError(`'${path}' already exists.`)
      return
    }
    const next = snapshot()
    const renamed = {
      ...current,
      content: next[index]?.content ?? current.content,
      path,
    }
    window.pcmonaco?.closeFile(editorId, current.path)
    next[index] = renamed
    setFiles(next)
    if (entrypoint === current.path) setEntrypoint(path)
    setRenaming(null)
    window.pcmonaco?.openFile(editorId, path, renamed.content, languageForPath(path))
  }
  function payload(): { entrypoint: string | null; files: JobCodeFile[] } {
    const next = snapshot()
    setFiles(next)
    return {
      entrypoint: entrypoint ?? (next.length > 1 ? (next[0]?.path ?? null) : null),
      files: next,
    }
  }
  async function deploy(): Promise<void> {
    setError(null)
    setMessage(null)
    try {
      const updated = await saveMutation.mutateAsync(payload())
      setJob(updated)
      setEntrypoint(updated.mapEntrypoint)
      setMessage('Deployed.')
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : 'The code could not be deployed.')
    }
  }
  async function run(): Promise<void> {
    setError(null)
    setMessage(null)
    try {
      const result = await runMutation.mutateAsync(payload())
      setJob(result.job)
      setEntrypoint(result.job.mapEntrypoint)
      setLastRun(result.run)
      setPanelOpen(true)
      setMessage(`Run ${result.run.status}.`)
    } catch (caught: unknown) {
      setError(caught instanceof Error ? caught.message : 'The job could not be tested.')
    }
  }
  if (job.mapSourceKind !== 'code')
    return (
      <div className="notice">
        This job runs a container image (<code>{job.mapImage}</code>), not inline code — there is
        nothing to edit here.{' '}
        <button
          onClick={() => {
            void navigate(`/project/${projectId}/jobs`)
          }}
          type="button"
        >
          Back to jobs
        </button>
      </div>
    )
  return (
    <div className="job-code-page-react">
      <title>PlaceContext — {job.name}</title>
      <header>
        <button
          aria-label="Back to Jobs"
          className="dcbtn"
          onClick={() => {
            void navigate(`/project/${projectId}/jobs`)
          }}
          type="button"
        >
          ←
        </button>
        <strong>{job.name}</strong>
        <span>{runtimeId}</span>
        {basic ? <small>basic editor (CDN unreachable)</small> : null}
        <i />
        {message === null ? null : <em>{message}</em>}
        <button className="dcbtn" disabled={busy} onClick={() => void run()} type="button">
          {runMutation.isPending ? 'Testing…' : '▶ Test'}
        </button>
        <button
          className="dcbtn primary"
          disabled={busy}
          onClick={() => void deploy()}
          type="button"
        >
          {saveMutation.isPending ? 'Deploying…' : 'Deploy'}
        </button>
      </header>
      {error === null ? null : (
        <div className="job-code-error" role="alert">
          {error}
        </div>
      )}
      <main>
        <aside>
          <header>
            <span>Files</span>
            <button
              aria-label="Add file"
              onClick={() => {
                setAdding(true)
              }}
              type="button"
            >
              ＋
            </button>
          </header>
          {adding ? (
            <div className="job-code-add">
              <input
                autoFocus
                className="dcinput"
                onChange={(event) => {
                  setNewPath(event.target.value)
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') add()
                  else if (event.key === 'Escape') setAdding(false)
                }}
                placeholder="path/name.js"
                value={newPath}
              />
              <button onClick={add} type="button">
                ✓
              </button>
            </div>
          ) : null}
          <div className="job-code-files">
            {files.map((file, index) => (
              <div
                className={index === active ? 'active' : ''}
                key={file.path}
                onClick={() => {
                  open(index)
                }}
              >
                {renaming === index ? (
                  <>
                    <input
                      autoFocus
                      className="dcinput"
                      onClick={(event) => {
                        event.stopPropagation()
                      }}
                      onChange={(event) => {
                        setRenamePath(event.target.value)
                      }}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') rename(index)
                        else if (event.key === 'Escape') setRenaming(null)
                      }}
                      value={renamePath}
                    />
                    <button
                      onClick={(event) => {
                        event.stopPropagation()
                        rename(index)
                      }}
                      type="button"
                    >
                      ✓
                    </button>
                  </>
                ) : (
                  <>
                    <span title={file.path}>{file.path}</span>
                    <button
                      aria-label={`Rename ${file.path}`}
                      onClick={(event) => {
                        event.stopPropagation()
                        setRenaming(index)
                        setRenamePath(file.path)
                      }}
                      type="button"
                    >
                      ✎
                    </button>
                  </>
                )}
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
                      remove(index)
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
            Entry: <code>{entrypoint ?? '(runtime default)'}</code>
          </footer>
        </aside>
        <section>
          <div className="job-editor-host" id={editorId} />
          <div className={panelOpen ? 'job-results-react open' : 'job-results-react'}>
            <button
              onClick={() => {
                setPanelOpen((value) => !value)
              }}
              type="button"
            >
              <span>Execution results</span>
              {lastRun === null ? null : (
                <b className={lastRun.status.toLocaleLowerCase()}>{lastRun.status}</b>
              )}
              <i />
              <span>{panelOpen ? '▾' : '▴'}</span>
            </button>
            {panelOpen ? (
              <div>
                {lastRun === null ? (
                  <span>
                    Click Test to deploy and run this job. Shard artifacts and logs appear here.
                  </span>
                ) : (
                  lastRun.shards.map((shard) => (
                    <article key={shard.index}>
                      <header>
                        <strong>Shard {shard.index}</strong>
                        <span className={shard.outcome.toLocaleLowerCase()}>{shard.outcome}</span>
                        <small>exit {shard.exitCode}</small>
                      </header>
                      {shard.artifact === null ? null : <pre>{pretty(shard.artifact)}</pre>}
                      {shard.log === null ? null : (
                        <details>
                          <summary>Log</summary>
                          <pre>{shard.log}</pre>
                        </details>
                      )}
                    </article>
                  ))
                )}
              </div>
            ) : null}
          </div>
        </section>
      </main>
    </div>
  )
}
