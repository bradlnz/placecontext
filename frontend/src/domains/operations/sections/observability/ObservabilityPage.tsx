import { useMutation, useQuery, useQueryClient, useSuspenseQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { NavLink, useSearchParams } from 'react-router-dom'

import { replayObservabilityRun } from '../../api/observability-api'
import {
  observabilityPageQueryOptions,
  observabilityQueryKeys,
  observabilityRunDetailsQueryOptions,
} from '../../api/observability-query'
import type {
  ChainReport,
  ChainStep,
  JobRun,
  JobRunTelemetry,
  ObservabilityRunDetails,
  RunArtifact,
  RunReport,
  TraceSpan,
} from '../../model/observability'

type ObservabilityTab = 'runs' | 'chains' | 'traces'

export function ObservabilityPage() {
  const pageQuery = useSuspenseQuery(observabilityPageQueryOptions)
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  const [tab, setTab] = useState<ObservabilityTab>(() =>
    searchParams.has('chainRun') ? 'chains' : 'runs',
  )
  const [message, setMessage] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const selectedRunId = searchParams.get('run')
  const selectedChainId = searchParams.get('chainRun')
  const selectedReport = pageQuery.data.runs.find((report) => report.run.id === selectedRunId)
  const selectedTrace = pageQuery.data.liveTraces.find((trace) => trace.runId === selectedRunId)
  const selectedChain = pageQuery.data.chains.find((report) => report.run.id === selectedChainId)
  const selectedJobId = selectedReport?.jobId ?? selectedTrace?.jobId ?? null
  const detailQuery = useQuery({
    ...observabilityRunDetailsQueryOptions(selectedRunId ?? '', selectedJobId ?? ''),
    enabled: selectedRunId !== null && selectedJobId !== null,
  })
  const replayMutation = useMutation({
    mutationFn: async (runId: string) => replayObservabilityRun(runId, AbortSignal.timeout(30_000)),
    onSuccess: async (result) => {
      setMessage(`Replay ${abbreviatedId(result.runId)} started — ${result.status}.`)
      closeRun()
      await queryClient.invalidateQueries({ queryKey: observabilityQueryKeys.page })
    },
    onError: (error: Error) => {
      setMessage(error.message)
    },
  })
  const activeItems =
    tab === 'chains'
      ? pageQuery.data.chains
      : tab === 'traces'
        ? pageQuery.data.liveTraces
        : pageQuery.data.runs
  const activeCount = activeItems.length
  const succeededCount = activeItems.filter((item) => itemStatus(item) === 'succeeded').length
  const runningCount = activeItems.filter((item) => itemStatus(item) === 'running').length
  const failedCount = activeItems.filter((item) => itemStatus(item) === 'failed').length
  const successPercent = activeCount === 0 ? 0 : Math.round((succeededCount * 100) / activeCount)

  function setParam(name: 'run' | 'chainRun', value: string | null): void {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current)
        if (value === null) next.delete(name)
        else next.set(name, value)
        if (name === 'run' && value !== null) next.delete('chainRun')
        if (name === 'chainRun' && value !== null) next.delete('run')
        return next
      },
      { replace: true },
    )
  }

  function openRun(runId: string): void {
    setParam('run', runId)
  }

  function closeRun(): void {
    setParam('run', null)
  }

  async function refresh(): Promise<void> {
    setRefreshing(true)
    setMessage(null)
    try {
      await queryClient.invalidateQueries({ queryKey: observabilityQueryKeys.page })
    } catch (error: unknown) {
      setMessage(error instanceof Error ? error.message : 'Observability could not be refreshed.')
    } finally {
      setRefreshing(false)
    }
  }

  return (
    <section className="observability-page-react">
      <title>PlaceContext — Observability</title>
      <header className="observability-head-react">
        <div>
          <h1>Observability</h1>
          <p>What ran, where, and how it went — across every project in the workspace.</p>
        </div>
        <button
          className="dcbtn"
          disabled={refreshing}
          onClick={() => void refresh()}
          type="button"
        >
          {refreshing ? 'Refreshing…' : 'Refresh'}
        </button>
      </header>

      <nav aria-label="Observability lenses" className="observability-tabs-react">
        <TabButton
          active={tab === 'runs'}
          count={pageQuery.data.runs.length}
          label="Job history"
          onClick={() => {
            setTab('runs')
          }}
        />
        <TabButton
          active={tab === 'chains'}
          count={pageQuery.data.chains.length}
          label="Chains"
          onClick={() => {
            setTab('chains')
          }}
        />
        <TabButton
          active={tab === 'traces'}
          count={pageQuery.data.liveTraces.length}
          label="Live traces"
          onClick={() => {
            setTab('traces')
          }}
        />
      </nav>

      {message === null ? null : (
        <div
          className={
            replayMutation.isError ? 'observability-message error' : 'observability-message'
          }
          role={replayMutation.isError ? 'alert' : 'status'}
        >
          {message}
        </div>
      )}

      {activeCount > 0 ? (
        <section aria-label="Run summary" className="observability-summary-react">
          <SummaryMetric
            label={tab === 'chains' ? 'chains' : tab === 'traces' ? 'traces' : 'runs'}
            value={activeCount}
          />
          <SummaryMetric label="running" tone="running" value={runningCount} />
          <SummaryMetric label="succeeded" tone="succeeded" value={succeededCount} />
          <SummaryMetric label="failed" tone="failed" value={failedCount} />
          <div
            aria-label={`${String(successPercent)}% succeeded`}
            className="observability-progress"
            title={`${String(successPercent)}% succeeded`}
          >
            <span style={{ width: `${String(successPercent)}%` }} />
          </div>
        </section>
      ) : null}

      {tab === 'runs' ? <RunCatalogue reports={pageQuery.data.runs} onOpen={openRun} /> : null}
      {tab === 'chains' ? (
        <ChainCatalogue
          reports={pageQuery.data.chains}
          onOpen={(report) => {
            setParam('chainRun', report.run.id)
          }}
        />
      ) : null}
      {tab === 'traces' ? (
        <TraceCatalogue
          traces={pageQuery.data.liveTraces}
          onOpen={(trace) => {
            openRun(trace.runId)
          }}
        />
      ) : null}

      {selectedChain === undefined ? null : (
        <ChainDrawer
          onClose={() => {
            setParam('chainRun', null)
          }}
          onOpenRun={(step) => {
            if (step.runId !== null) openRun(step.runId)
          }}
          report={selectedChain}
        />
      )}

      {selectedRunId === null || selectedJobId === null ? null : (
        <RunDrawer
          canReplay={pageQuery.data.canReplay}
          details={detailQuery.data}
          error={detailQuery.error instanceof Error ? detailQuery.error.message : null}
          loading={detailQuery.isPending}
          onClose={closeRun}
          onReplay={(runId) => {
            replayMutation.mutate(runId)
          }}
          replaying={replayMutation.isPending}
          report={selectedReport}
          runId={selectedRunId}
          trace={selectedTrace}
        />
      )}
    </section>
  )
}

function TabButton({
  active,
  count,
  label,
  onClick,
}: {
  active: boolean
  count: number
  label: string
  onClick: () => void
}) {
  return (
    <button className={active ? 'active' : undefined} onClick={onClick} type="button">
      {label} <span>{count}</span>
    </button>
  )
}

function SummaryMetric({ label, tone, value }: { label: string; tone?: string; value: number }) {
  return (
    <div className={tone}>
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  )
}

function RunCatalogue({
  reports,
  onOpen,
}: {
  reports: RunReport[]
  onOpen: (runId: string) => void
}) {
  if (reports.length === 0) {
    return (
      <EmptyState>
        No job runs recorded yet — run a job from any project&apos;s Jobs tab and it appears here.
      </EmptyState>
    )
  }
  return (
    <section className="dccard observability-catalog-react">
      <header>
        <div>
          <span>⌄</span>
          <strong>Job run catalogue</strong>
          <small>{reports.length} across the workspace</small>
        </div>
        <small>
          {reports.filter((report) => normalizedStatus(report.run.status) === 'succeeded').length}{' '}
          succeeded
        </small>
      </header>
      <div>
        {reports.map((report) => (
          <button
            key={report.run.id}
            onClick={() => {
              onOpen(report.run.id)
            }}
            type="button"
          >
            <StatusPill status={report.run.status} />
            <div>
              <strong>{report.jobName}</strong>
              <small>{report.projectName}</small>
            </div>
            <span>
              ✓ {succeededShardCount(report.run)}
              {failedShardCount(report.run) > 0
                ? ` · ✗ ${String(failedShardCount(report.run))}`
                : ''}
            </span>
            <time>{formatDate(report.run.startedAt)}</time>
            <span>{durationBetween(report.run.startedAt, report.run.finishedAt)}</span>
          </button>
        ))}
      </div>
    </section>
  )
}

function ChainCatalogue({
  reports,
  onOpen,
}: {
  reports: ChainReport[]
  onOpen: (report: ChainReport) => void
}) {
  if (reports.length === 0) {
    return (
      <EmptyState>
        No chain runs recorded yet — run a chain from any project&apos;s Chains tab and it appears
        here.
      </EmptyState>
    )
  }
  return (
    <section className="dccard observability-catalog-react">
      <header>
        <div>
          <span>⌄</span>
          <strong>Chain run catalogue</strong>
          <small>{reports.length} across the workspace</small>
        </div>
        <small>
          {reports.filter((report) => normalizedStatus(report.run.status) === 'succeeded').length}{' '}
          succeeded
        </small>
      </header>
      <div>
        {reports.map((report) => (
          <button
            key={report.run.id}
            onClick={() => {
              onOpen(report)
            }}
            type="button"
          >
            <StatusPill status={report.run.status} />
            <div>
              <strong>{report.run.chainName}</strong>
              <small>{report.projectName}</small>
            </div>
            <span>
              {
                report.run.steps.filter((step) => normalizedStatus(step.status) === 'succeeded')
                  .length
              }
              /{report.run.steps.length} steps
            </span>
            <time>{formatDate(report.run.startedAt)}</time>
            <span>{durationBetween(report.run.startedAt, report.run.finishedAt)}</span>
          </button>
        ))}
      </div>
    </section>
  )
}

function TraceCatalogue({
  traces,
  onOpen,
}: {
  traces: JobRunTelemetry[]
  onOpen: (trace: JobRunTelemetry) => void
}) {
  if (traces.length === 0) {
    return <EmptyState>No live traces on this replica yet — run a job and refresh.</EmptyState>
  }
  return (
    <>
      <p className="observability-trace-note">
        In-process OpenTelemetry spans from this Host replica (job.run → job.shard).
      </p>
      <section className="dccard observability-catalog-react">
        <header>
          <div>
            <span>⌄</span>
            <strong>Live trace catalogue</strong>
            <small>{traces.length} on this replica</small>
          </div>
          <small>
            {traces.filter((trace) => normalizedStatus(trace.status) === 'succeeded').length}{' '}
            succeeded
          </small>
        </header>
        <div>
          {traces.map((trace) => (
            <button
              key={trace.runId}
              onClick={() => {
                onOpen(trace)
              }}
              type="button"
            >
              <StatusPill status={trace.status ?? 'Unknown'} />
              <div>
                <strong>{trace.jobName ?? abbreviatedId(trace.jobId)}</strong>
                <small>
                  trace {trace.traceId === null ? '—' : `${trace.traceId.slice(0, 8)}…`}
                </small>
              </div>
              <span>{trace.shards.length} shards</span>
              <time>{formatDate(trace.startedAt)}</time>
              <span>{formatMilliseconds(trace.durationMs)}</span>
            </button>
          ))}
        </div>
      </section>
    </>
  )
}

function EmptyState({ children }: { children: React.ReactNode }) {
  return <div className="dccard observability-empty-react">{children}</div>
}

function ChainDrawer({
  report,
  onClose,
  onOpenRun,
}: {
  report: ChainReport
  onClose: () => void
  onOpenRun: (step: ChainStep) => void
}) {
  const stages = groupStages(report.run.steps)
  return (
    <div className="observability-drawer-backdrop" onClick={onClose} role="presentation">
      <aside
        aria-label={`${report.run.chainName} chain run details`}
        aria-modal="true"
        className="observability-drawer-react"
        onClick={(event) => {
          event.stopPropagation()
        }}
        role="dialog"
      >
        <header>
          <div>
            <strong>{report.run.chainName}</strong>
            <small>
              {report.projectName} · chain run {abbreviatedId(report.run.id)} ·{' '}
              {formatDate(report.run.startedAt)}
            </small>
          </div>
          <NavLink className="dcbtn" to={`/project/${report.projectId}/chains`}>
            Open project →
          </NavLink>
          <button aria-label="Close chain run details" onClick={onClose} type="button">
            ×
          </button>
        </header>
        <div className="observability-drawer-body">
          <div className="observability-status-line">
            <StatusPill large status={report.run.status} />
            <span>
              {
                report.run.steps.filter((step) => normalizedStatus(step.status) === 'succeeded')
                  .length
              }
              /{report.run.steps.length} steps succeeded ·{' '}
              {durationBetween(report.run.startedAt, report.run.finishedAt)}
            </span>
          </div>
          <div className="observability-pipeline-react">
            {stages.map((stage, index) => (
              <div className="observability-stage-wrap" key={stage[0]?.stageIndex ?? index}>
                {index === 0 ? null : <span aria-hidden="true">──▶</span>}
                <div>
                  {stage.map((step) => (
                    <button
                      className={statusTone(step.status)}
                      disabled={step.runId === null}
                      key={`${String(step.stageIndex)}-${String(step.branchIndex)}-${step.jobId}`}
                      onClick={() => {
                        onOpenRun(step)
                      }}
                      type="button"
                    >
                      <strong>
                        {statusIcon(step.status)} {step.jobName}
                      </strong>
                      <small>
                        {step.error ??
                          `${formatDate(step.startedAt)} · ${durationBetween(step.startedAt, step.finishedAt)}`}
                      </small>
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
          {report.run.finalOutput === null || report.run.finalOutput.length === 0 ? null : (
            <section className="dccard observability-panel-react">
              <h2>Pipeline output</h2>
              <pre>{prettyJson(report.run.finalOutput)}</pre>
            </section>
          )}
        </div>
      </aside>
    </div>
  )
}

function RunDrawer({
  canReplay,
  details,
  error,
  loading,
  onClose,
  onReplay,
  replaying,
  report,
  runId,
  trace,
}: {
  canReplay: boolean
  details: ObservabilityRunDetails | undefined
  error: string | null
  loading: boolean
  onClose: () => void
  onReplay: (runId: string) => void
  replaying: boolean
  report: RunReport | undefined
  runId: string
  trace: JobRunTelemetry | undefined
}) {
  const run = report?.run
  const telemetry = details?.telemetry ?? trace
  return (
    <div className="observability-drawer-backdrop" onClick={onClose} role="presentation">
      <aside
        aria-label={`${report?.jobName ?? trace?.jobName ?? 'Job'} run details`}
        aria-modal="true"
        className="observability-drawer-react"
        onClick={(event) => {
          event.stopPropagation()
        }}
        role="dialog"
      >
        <header>
          <div>
            <strong>{report?.jobName ?? trace?.jobName ?? 'Job run'}</strong>
            <small>
              {report?.projectName ?? 'Live trace'} · run {abbreviatedId(runId)} ·{' '}
              {formatDate(run?.startedAt ?? trace?.startedAt ?? null)}
            </small>
          </div>
          {canReplay && run !== undefined ? (
            <button
              className="dcbtn"
              disabled={replaying}
              onClick={() => {
                onReplay(run.id)
              }}
              type="button"
            >
              {replaying ? 'Replaying…' : '↻ Replay'}
            </button>
          ) : null}
          {run === undefined ? null : (
            <NavLink className="dcbtn" to={`/project/${run.projectId}/jobs`}>
              Open project →
            </NavLink>
          )}
          <button aria-label="Close run details" onClick={onClose} type="button">
            ×
          </button>
        </header>
        <div className="observability-drawer-body">
          {run === undefined ? null : <RunSnapshot run={run} />}
          {loading ? <p className="observability-detail-note">Loading run details…</p> : null}
          {error === null ? null : (
            <p className="observability-detail-note error" role="alert">
              {error}
            </p>
          )}
          {details?.artifacts === undefined || details.artifacts.length === 0 ? null : (
            <section className="dccard observability-panel-react">
              <h2>Post-job outputs</h2>
              <div className="observability-output-list">
                {details.artifacts.map((artifact) => (
                  <div key={artifact.id}>
                    <span>{artifact.kind}</span>
                    <a
                      href={`/runs/${artifact.runId}/artifacts/${artifact.id}`}
                      rel="noopener"
                      target="_blank"
                    >
                      {artifact.title} ↗
                    </a>
                    <small>{formatBytes(artifact.sizeBytes)}</small>
                  </div>
                ))}
              </div>
            </section>
          )}
          {details !== undefined && details.traceSpans.length > 0 ? (
            <section className="dccard observability-panel-react">
              <header>
                <h2>Full trace</h2>
                <small>
                  {telemetry?.traceId ?? ''} · {formatMilliseconds(telemetry?.durationMs ?? null)}{' '}
                  total
                </small>
              </header>
              <div className="observability-waterfall-react">
                {details.traceSpans.map((span) => (
                  <TraceSpanRow
                    key={`${span.spanId ?? span.name}-${span.startedAt}`}
                    parentDuration={span.durationMs}
                    span={span}
                  />
                ))}
              </div>
            </section>
          ) : telemetry === undefined ? null : (
            <TelemetrySummary telemetry={telemetry} />
          )}
          {run === undefined ? null : <RunResults run={run} />}
        </div>
      </aside>
    </div>
  )
}

function RunSnapshot({ run }: { run: JobRun }) {
  return (
    <div className="observability-status-line">
      <StatusPill large status={run.status} />
      <span>
        Ran as <code>{run.snapshot.mapSourceKind}</code> · {run.snapshot.mapSourceLabel} ·{' '}
        {run.snapshot.shardCount} shards · concurrency {run.snapshot.concurrencyLimit}
      </span>
    </div>
  )
}

function TelemetrySummary({ telemetry }: { telemetry: JobRunTelemetry }) {
  return (
    <section className="dccard observability-panel-react">
      <header>
        <h2>OpenTelemetry summary</h2>
        <small>
          {formatMilliseconds(telemetry.durationMs)} total{telemetry.replay ? ' · replay' : ''}
        </small>
      </header>
      <div className="observability-shard-chips">
        {telemetry.shards
          .toSorted((left, right) => left.index - right.index)
          .map((shard) => (
            <span key={shard.index}>
              shard {shard.index}{' '}
              <b className={statusTone(shard.outcome)}>{shard.outcome ?? 'unknown'}</b> ·{' '}
              {formatMilliseconds(shard.durationMs)}
            </span>
          ))}
      </div>
    </section>
  )
}

function TraceSpanRow({ parentDuration, span }: { parentDuration: number; span: TraceSpan }) {
  const width =
    parentDuration <= 0 ? 100 : Math.max(3, Math.min(100, (span.durationMs / parentDuration) * 100))
  return (
    <div className="observability-span-react">
      <div>
        <strong>{span.name}</strong>
        <small>
          {formatMilliseconds(span.durationMs)} · {span.spanId ?? 'no span id'}
        </small>
      </div>
      <div className="observability-span-track">
        <span style={{ width: `${String(width)}%` }} />
      </div>
      {Object.keys(span.tags).length === 0 ? null : (
        <div className="observability-trace-tags">
          {Object.entries(span.tags).map(([key, value]) => (
            <span key={key}>
              {key}={value}
            </span>
          ))}
        </div>
      )}
      {span.children.length === 0 ? null : (
        <div className="observability-span-children">
          {span.children.map((child) => (
            <TraceSpanRow
              key={`${child.spanId ?? child.name}-${child.startedAt}`}
              parentDuration={span.durationMs}
              span={child}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function RunResults({ run }: { run: JobRun }) {
  return (
    <>
      {run.shardResults.map((shard) => (
        <section className="observability-result-react" key={shard.index}>
          <header>
            <strong>Shard {shard.index}</strong>
            <StatusPill status={shard.outcome} />
            <span>exit {shard.exitCode}</span>
          </header>
          {shard.artifact === null ? null : <pre>{prettyJson(shard.artifact)}</pre>}
          <ArtifactDownloads artifacts={shard.artifacts} />
          {shard.log === null || shard.log.trim().length === 0 ? null : (
            <details open={normalizedStatus(shard.outcome) === 'failed'}>
              <summary>Log</summary>
              <pre>{shard.log}</pre>
            </details>
          )}
        </section>
      ))}
      {run.reduceResult === null ? null : (
        <section className="observability-result-react reduce">
          <header>
            <strong>Reduce step</strong>
            <StatusPill status={run.reduceResult.succeeded ? 'Succeeded' : 'Failed'} />
            <span>exit {run.reduceResult.exitCode}</span>
          </header>
          {run.reduceResult.artifact === null ? null : (
            <pre>{prettyJson(run.reduceResult.artifact)}</pre>
          )}
          <ArtifactDownloads artifacts={run.reduceResult.artifacts} />
          {run.reduceResult.log === null || run.reduceResult.log.trim().length === 0 ? null : (
            <details>
              <summary>Log</summary>
              <pre>{run.reduceResult.log}</pre>
            </details>
          )}
        </section>
      )}
    </>
  )
}

function ArtifactDownloads({ artifacts }: { artifacts: RunArtifact[] }) {
  if (artifacts.length === 0) return null
  return (
    <div className="observability-downloads-react">
      {artifacts.map((artifact) => (
        <a
          className="dcbtn"
          download={artifact.name}
          href={artifactDataUri(artifact)}
          key={artifact.name}
        >
          ↓ {artifact.name}
        </a>
      ))}
    </div>
  )
}

function StatusPill({ large = false, status }: { large?: boolean; status: string }) {
  return (
    <span className={`observability-status-react ${statusTone(status)}${large ? ' large' : ''}`}>
      {status}
    </span>
  )
}

function groupStages(steps: ChainStep[]): ChainStep[][] {
  const stages = new Map<number, ChainStep[]>()
  for (const step of steps)
    stages.set(step.stageIndex, [...(stages.get(step.stageIndex) ?? []), step])
  return [...stages.entries()]
    .toSorted(([left], [right]) => left - right)
    .map(([, stage]) => stage.toSorted((left, right) => left.branchIndex - right.branchIndex))
}

function itemStatus(item: RunReport | ChainReport | JobRunTelemetry): string {
  return normalizedStatus('run' in item ? item.run.status : item.status)
}

function normalizedStatus(status: string | null): string {
  return (status ?? 'unknown').trim().toLowerCase()
}

function statusTone(status: string | null): string {
  const normalized = normalizedStatus(status)
  if (normalized === 'succeeded' || normalized === 'completed') return 'succeeded'
  if (normalized === 'failed' || normalized === 'cancelled') return 'failed'
  if (normalized === 'running' || normalized === 'queued') return 'running'
  if (normalized === 'partial') return 'partial'
  return 'neutral'
}

function statusIcon(status: string): string {
  const tone = statusTone(status)
  return tone === 'succeeded' ? '✓' : tone === 'failed' ? '×' : tone === 'running' ? '●' : '○'
}

function succeededShardCount(run: JobRun): number {
  return run.shardResults.filter((shard) => normalizedStatus(shard.outcome) === 'succeeded').length
}

function failedShardCount(run: JobRun): number {
  return run.shardResults.filter((shard) => normalizedStatus(shard.outcome) === 'failed').length
}

function abbreviatedId(value: string): string {
  return value.slice(0, 8)
}

function formatDate(value: string | null): string {
  if (value === null) return '—'
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  )
}

function durationBetween(start: string | null, end: string | null): string {
  if (start === null) return 'not started'
  if (end === null) return 'running…'
  return formatMilliseconds(new Date(end).getTime() - new Date(start).getTime())
}

function formatMilliseconds(value: number | null): string {
  if (value === null || !Number.isFinite(value)) return '—'
  if (value < 1_000) return `${value.toFixed(0)} ms`
  if (value < 60_000) return `${(value / 1_000).toFixed(value < 10_000 ? 1 : 0)} s`
  return `${(value / 60_000).toFixed(1)} m`
}

function formatBytes(value: number): string {
  if (value >= 1_048_576) return `${(value / 1_048_576).toFixed(1)} MB`
  if (value >= 1_024) return `${(value / 1_024).toFixed(1)} KB`
  return `${String(value)} B`
}

function prettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function artifactDataUri(artifact: RunArtifact): string {
  if (artifact.isBinary) return `data:application/octet-stream;base64,${artifact.content}`
  const contentType = artifact.name.toLowerCase().endsWith('.json')
    ? 'application/json'
    : 'text/plain'
  return `data:${contentType};charset=utf-8,${encodeURIComponent(artifact.content)}`
}
