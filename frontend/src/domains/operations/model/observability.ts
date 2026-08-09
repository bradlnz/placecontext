export interface RunArtifact {
  name: string
  content: string
  isBinary: boolean
}

export interface ShardResult {
  index: number
  exitCode: number
  outcome: string
  artifact: string | null
  log: string | null
  artifacts: RunArtifact[]
}

export interface ReduceResult {
  exitCode: number
  succeeded: boolean
  artifact: string | null
  log: string | null
  artifacts: RunArtifact[]
}

export interface JobRun {
  id: string
  jobId: string
  projectId: string
  status: string
  startedAt: string
  finishedAt: string | null
  shardResults: ShardResult[]
  reduceResult: ReduceResult | null
  snapshot: {
    mapSourceKind: string
    mapSourceLabel: string
    reduceSourceKind: string | null
    reduceSourceLabel: string | null
    concurrencyLimit: number
    shardCount: number
    allowNetworkEgress: boolean
  }
  attemptNumber: number
  originalRunId: string | null
}

export interface RunReport {
  jobId: string
  jobName: string
  projectName: string
  run: JobRun
}

export interface ChainStep {
  index: number
  stageIndex: number
  branchIndex: number
  jobId: string
  jobName: string
  runId: string | null
  status: string
  startedAt: string | null
  finishedAt: string | null
  error: string | null
}

export interface ChainReport {
  projectId: string
  projectName: string
  run: {
    id: string
    chainId: string
    chainName: string
    status: string
    steps: ChainStep[]
    finalOutput: string | null
    startedAt: string
    finishedAt: string | null
  }
}

export interface ShardTelemetry {
  index: number
  outcome: string | null
  exitCode: number | null
  durationMs: number | null
}

export interface JobRunTelemetry {
  runId: string
  jobId: string
  jobName: string | null
  projectId: string | null
  status: string | null
  replay: boolean
  startedAt: string
  durationMs: number | null
  shards: ShardTelemetry[]
  traceId: string | null
  spanId: string | null
}

export interface TraceSpan {
  name: string
  traceId: string | null
  spanId: string | null
  parentSpanId: string | null
  startedAt: string
  durationMs: number
  tags: Record<string, string>
  children: TraceSpan[]
}

export interface RunArtifactLink {
  id: string
  runId: string
  kind: string
  title: string
  contentType: string
  sizeBytes: number
  createdAt: string
}

export interface ObservabilityPageModel {
  runs: RunReport[]
  chains: ChainReport[]
  liveTraces: JobRunTelemetry[]
  canReplay: boolean
}

export interface ObservabilityRunDetails {
  artifacts: RunArtifactLink[]
  telemetry: JobRunTelemetry | null
  traceSpans: TraceSpan[]
}
