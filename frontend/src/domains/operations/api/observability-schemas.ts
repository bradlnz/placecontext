import { z } from 'zod'

import type { TraceSpan } from '../model/observability'

const date = z.iso.datetime({ offset: true })
const runArtifactSchema = z.object({ name: z.string(), content: z.string(), isBinary: z.boolean() })
const shardResultSchema = z.object({
  index: z.number().int(),
  exitCode: z.number().int(),
  outcome: z.string(),
  artifact: z.string().nullable(),
  log: z.string().nullable(),
  artifacts: z.array(runArtifactSchema),
})
const reduceResultSchema = z.object({
  exitCode: z.number().int(),
  succeeded: z.boolean(),
  artifact: z.string().nullable(),
  log: z.string().nullable(),
  artifacts: z.array(runArtifactSchema),
})
const jobRunSchema = z.object({
  id: z.uuid(),
  jobId: z.uuid(),
  projectId: z.uuid(),
  status: z.string(),
  startedAt: date,
  finishedAt: date.nullable(),
  shardResults: z.array(shardResultSchema),
  reduceResult: reduceResultSchema.nullable(),
  snapshot: z.object({
    mapSourceKind: z.string(),
    mapSourceLabel: z.string(),
    reduceSourceKind: z.string().nullable(),
    reduceSourceLabel: z.string().nullable(),
    concurrencyLimit: z.number().int(),
    shardCount: z.number().int(),
    allowNetworkEgress: z.boolean(),
  }),
  attemptNumber: z.number().int(),
  originalRunId: z.uuid().nullable(),
})
const runReportSchema = z.object({
  jobId: z.uuid(),
  jobName: z.string(),
  projectName: z.string(),
  run: jobRunSchema,
})
const chainStepSchema = z.object({
  index: z.number().int(),
  stageIndex: z.number().int(),
  branchIndex: z.number().int(),
  jobId: z.uuid(),
  jobName: z.string(),
  runId: z.uuid().nullable(),
  status: z.string(),
  startedAt: date.nullable(),
  finishedAt: date.nullable(),
  error: z.string().nullable(),
})
const chainReportSchema = z.object({
  projectId: z.uuid(),
  projectName: z.string(),
  run: z.object({
    id: z.uuid(),
    chainId: z.uuid(),
    chainName: z.string(),
    status: z.string(),
    steps: z.array(chainStepSchema),
    finalOutput: z.string().nullable(),
    startedAt: date,
    finishedAt: date.nullable(),
  }),
})
const shardTelemetrySchema = z.object({
  index: z.number().int(),
  outcome: z.string().nullable(),
  exitCode: z.number().int().nullable(),
  durationMs: z.number().nullable(),
})
export const jobRunTelemetrySchema = z.object({
  runId: z.uuid(),
  jobId: z.uuid(),
  jobName: z.string().nullable(),
  projectId: z.uuid().nullable(),
  status: z.string().nullable(),
  replay: z.boolean(),
  startedAt: date,
  durationMs: z.number().nullable(),
  shards: z.array(shardTelemetrySchema),
  traceId: z.string().nullable(),
  spanId: z.string().nullable(),
})

export const traceSpanSchema: z.ZodType<TraceSpan> = z.lazy(() =>
  z.object({
    name: z.string(),
    traceId: z.string().nullable(),
    spanId: z.string().nullable(),
    parentSpanId: z.string().nullable(),
    startedAt: date,
    durationMs: z.number(),
    tags: z.record(z.string(), z.string()),
    children: z.array(traceSpanSchema),
  }),
)

export const observabilityPageSchema = z.object({
  runs: z.array(runReportSchema),
  chains: z.array(chainReportSchema),
  liveTraces: z.array(jobRunTelemetrySchema),
  canReplay: z.boolean(),
})

export const observabilityRunDetailsSchema = z.object({
  artifacts: z.array(
    z.object({
      id: z.uuid(),
      runId: z.uuid(),
      kind: z.string(),
      title: z.string(),
      contentType: z.string(),
      sizeBytes: z.number().int().nonnegative(),
      createdAt: date,
    }),
  ),
  telemetry: jobRunTelemetrySchema.nullable(),
  traceSpans: z.array(traceSpanSchema),
})

export const observabilityJobRunDetailsSchema = observabilityRunDetailsSchema.omit({
  artifacts: true,
})

export const observabilityRunArtifactsSchema = observabilityRunDetailsSchema.shape.artifacts

export const replayObservabilityRunSchema = z.object({ runId: z.uuid(), status: z.string() })
