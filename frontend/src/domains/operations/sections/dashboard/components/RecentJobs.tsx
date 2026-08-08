import { useState } from 'react'

import type { DashboardRun } from '../../../model/dashboard'

type RunFilter = 'all' | 'running' | 'failed'

interface RecentJobsProps {
  runs: DashboardRun[]
}

const FILTERS: readonly RunFilter[] = ['all', 'running', 'failed']

function formatDuration(run: DashboardRun): string {
  const end = run.finishedAt === null ? Date.now() : new Date(run.finishedAt).getTime()
  const seconds = Math.max(0, Math.round((end - new Date(run.startedAt).getTime()) / 1000))
  if (seconds < 60) return `${String(seconds)}s`

  const minutes = Math.floor(seconds / 60)
  const remainder = seconds % 60
  return `${String(minutes)}m ${String(remainder)}s`
}

function formatStarted(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(value))
}

export function RecentJobs({ runs }: RecentJobsProps) {
  const [filter, setFilter] = useState<RunFilter>('all')
  const filteredRuns = runs.filter((run) => filter === 'all' || run.status.toLowerCase() === filter)

  async function handleFilterChanged(nextFilter: RunFilter): Promise<void> {
    await Promise.resolve()
    setFilter(nextFilter)
  }

  return (
    <section aria-labelledby="recent-jobs-title" className="dccard dashboard-jobs-card">
      <div className="jobs-head">
        <h2 className="jobs-title" id="recent-jobs-title">
          Recent jobs
        </h2>
        <div className="jobs-filters">
          {FILTERS.map((option) => (
            <button
              aria-pressed={filter === option}
              className="jobs-filter"
              key={option}
              onClick={() => void handleFilterChanged(option)}
              type="button"
            >
              {option}
            </button>
          ))}
        </div>
      </div>
      <div className="jobs-scroll">
        <div aria-hidden="true" className="jobs-grid-head">
          <span>STATUS</span>
          <span>JOB</span>
          <span>PROJECT</span>
          <span>SHARDS</span>
          <span>DURATION</span>
          <span>STARTED</span>
          <span>RETURN</span>
        </div>
        {filteredRuns.length === 0 ? (
          <div className="jobs-empty">
            No runs yet — submit a job and its run appears here live.
          </div>
        ) : (
          filteredRuns.map((run) => (
            <a className="dashboard-jobs-row" href={`/observability?run=${run.id}`} key={run.id}>
              <span className={`job-status status-${run.status.toLowerCase()}`}>
                <span className="job-status-dot" />
                {run.status.toUpperCase()}
              </span>
              <span className="job-name">{run.jobName}</span>
              <span className="job-project">{run.projectName}</span>
              <span className="job-cell">
                ✓{run.succeededShards}
                {run.failedShards > 0 ? ` ✗${String(run.failedShards)}` : ''}
              </span>
              <span className="job-cell">{formatDuration(run)}</span>
              <time className="job-started" dateTime={run.startedAt}>
                {formatStarted(run.startedAt)}
              </time>
              <span className="job-return">{run.sourceKind}</span>
            </a>
          ))
        )}
      </div>
    </section>
  )
}
