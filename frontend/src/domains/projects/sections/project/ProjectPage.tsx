import { useMutation, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'

import { updateProjectRequirements } from '../../api/project-page-api'
import { projectPageQueryOptions } from '../../api/project-page-query'

type ProjectTab = 'overview' | 'requirements' | 'activity'

function normalizeTab(tab: string | null): ProjectTab {
  return tab === 'requirements' || tab === 'activity' ? tab : 'overview'
}

function languageColor(path: string): string {
  return path.endsWith('.rs') ? '#e3651f' : '#8b7cff'
}

export function ProjectPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const queryOptions = projectPageQueryOptions(projectId)
  const { data } = useSuspenseQuery(queryOptions)
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<ProjectTab>(() => normalizeTab(searchParams.get('tab')))
  const [requirementsDraft, setRequirementsDraft] = useState(data.requirements?.markdown ?? '')
  const [requirementsSaved, setRequirementsSaved] = useState(data.requirements?.markdown ?? '')
  const [message, setMessage] = useState<string | null>(data.message)
  const mutation = useMutation({
    mutationFn: async (markdown: string) =>
      updateProjectRequirements(projectId, markdown, AbortSignal.timeout(30_000)),
    onSuccess: (requirements) => {
      setRequirementsDraft(requirements.markdown)
      setRequirementsSaved(requirements.markdown)
      setMessage('Project requirements saved.')
      queryClient.setQueryData(queryOptions.queryKey, {
        ...data,
        requirements,
      })
    },
  })

  useEffect(() => {
    const id = window.location.hash.slice(1)
    if (id !== '') document.getElementById(id)?.scrollIntoView()
  }, [])

  async function saveRequirements(): Promise<void> {
    setMessage(null)
    try {
      await mutation.mutateAsync(requirementsDraft)
    } catch (error: unknown) {
      setMessage(
        error instanceof Error ? error.message : 'Project requirements could not be saved.',
      )
    }
  }

  async function switchTab(nextTab: ProjectTab): Promise<void> {
    await Promise.resolve()
    setTab(nextTab)
  }

  async function backToProjects(): Promise<void> {
    await navigate('/')
  }

  return (
    <div className="project-page">
      <title>PlaceContext — Project</title>
      <button className="dcbtn project-back" onClick={() => void backToProjects()} type="button">
        ← All projects
      </button>
      <header className="project-page-head">
        <div>
          <div className="project-title-row">
            <span style={{ background: languageColor(data.overview.path) }} />
            <h1>{data.overview.name}</h1>
            <small>{data.overview.status}</small>
          </div>
          <div className="project-page-path">{data.overview.path}</div>
        </div>
      </header>
      {message === null ? null : (
        <div className="project-page-message" role="status">
          {message}
        </div>
      )}
      <div className="dctabs project-tabs">
        {(['overview', 'requirements', 'activity'] as const).map((item) => (
          <button
            className={tab === item ? 'dctab active' : 'dctab'}
            key={item}
            onClick={() => void switchTab(item)}
            type="button"
          >
            {item.slice(0, 1).toUpperCase() + item.slice(1)}
          </button>
        ))}
      </div>
      {tab === 'overview' ? (
        <section className="dccard project-section project-god-section">
          <header>
            <strong>God-nodes</strong>
            <small>top degree</small>
          </header>
          {data.overview.godNodes.length === 0 ? (
            <p>None detected.</p>
          ) : (
            <div>
              {data.overview.godNodes.map((node) => (
                <div className="project-god-row" key={node.id}>
                  <span />
                  <strong>{node.label}</strong>
                  <small>{node.degree} edges</small>
                </div>
              ))}
            </div>
          )}
        </section>
      ) : null}
      {tab === 'requirements' ? (
        <section className="dccard project-section">
          <header className="project-requirements-head">
            <div>
              <strong>Requirements</strong>
              <small>project-specific · added on top of the global requirements</small>
            </div>
            <div>
              {data.requirements?.updatedAtDisplay === null ||
              data.requirements?.updatedAtDisplay === undefined ? null : (
                <small>updated {data.requirements.updatedAtDisplay}</small>
              )}
              <button
                className="dcbtn primary"
                disabled={mutation.isPending || requirementsDraft === requirementsSaved}
                onClick={() => void saveRequirements()}
                type="button"
              >
                {mutation.isPending ? 'saving…' : 'Save requirements'}
              </button>
            </div>
          </header>
          <div className="project-requirements-body">
            {data.requirements === null ? (
              <p>Requirements could not be loaded.</p>
            ) : (
              <textarea
                onChange={(event) => {
                  setRequirementsDraft(event.target.value)
                }}
                placeholder="Requirements specific to this project (Markdown) — frameworks, patterns, constraints the review & skill prompts should enforce here."
                spellCheck={false}
                value={requirementsDraft}
              />
            )}
          </div>
        </section>
      ) : null}
      {tab === 'activity' ? (
        <>
          <section className="dccard project-section" id="decisions">
            <h2>Decisions</h2>
            {data.decisions === null ? (
              <p>Decisions could not be loaded.</p>
            ) : data.decisions.length === 0 ? (
              <p>
                No decisions recorded yet — they flow in through the <code>add_decision</code> MCP
                tool.
              </p>
            ) : (
              data.decisions.map((decision) => (
                <article
                  className="project-decision"
                  id={`decision-${decision.id}`}
                  key={decision.id}
                >
                  <header>
                    <strong>{decision.question}</strong>
                    <small>{decision.decidedAtDisplay}</small>
                  </header>
                  <div>→ {decision.choice}</div>
                  {decision.rationale.trim() === '' || decision.rationale === '(none)' ? null : (
                    <p>{decision.rationale}</p>
                  )}
                </article>
              ))
            )}
          </section>
          <section className="dccard project-section" id="changes">
            <h2>Recent changes</h2>
            {data.timeline === null ? (
              <p>Timeline could not be loaded.</p>
            ) : data.timeline.changes.length === 0 ? (
              <p>
                No changes recorded yet — they flow in through the <code>record_activity</code> MCP
                tool.
              </p>
            ) : (
              data.timeline.changes.map((change) => (
                <article className="project-change" key={change.id}>
                  <span>#{change.sequence}</span>
                  <strong className={change.kind === 'Agent' ? 'agent' : 'human'}>
                    {change.kind.toLocaleLowerCase()}
                  </strong>
                  <div>{change.title}</div>
                  <small>{change.commit ?? '—'}</small>
                </article>
              ))
            )}
          </section>
        </>
      ) : null}
    </div>
  )
}
