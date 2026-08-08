import { z } from 'zod'
const file = z.object({ path: z.string(), content: z.string() })
const parameter = z.object({
  name: z.string(),
  label: z.string().nullable(),
  required: z.boolean(),
  type: z.string(),
  options: z.array(z.string()).nullable(),
})
export const job = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  description: z.string().nullable(),
  mapSourceKind: z.string(),
  mapImage: z.string().nullable(),
  mapRuntimeId: z.string().nullable(),
  mapSource: z.string().nullable(),
  mapEntrypoint: z.string().nullable(),
  mapFiles: z.array(file),
  inputPayloads: z.array(z.string()),
  mapEnv: z.record(z.string(), z.string()),
  reduceSourceKind: z.string().nullable(),
  reduceImage: z.string().nullable(),
  reduceRuntimeId: z.string().nullable(),
  reduceSource: z.string().nullable(),
  reduceEntrypoint: z.string().nullable(),
  reduceFiles: z.array(file),
  reduceEnv: z.record(z.string(), z.string()).nullable(),
  concurrencyLimit: z.number(),
  successExitCodes: z.array(z.number()),
  partialExitCodes: z.array(z.number()),
  allowNetworkEgress: z.boolean(),
  allowApiInvocation: z.boolean(),
  parameters: z.array(parameter),
  postJobActions: z.array(z.string()),
  returnType: z.string(),
  returnFileName: z.string().nullable(),
  retryCount: z.number(),
  retryDelaySeconds: z.number(),
  mcpConnectionIds: z.array(z.uuid()),
  createdAt: z.iso.datetime({ offset: true }),
  updatedAt: z.iso.datetime({ offset: true }),
})
const trigger = z.object({
  id: z.uuid(),
  jobId: z.uuid().nullable(),
  name: z.string(),
  kind: z.string(),
  enabled: z.boolean(),
  cronExpression: z.string().nullable(),
  eventName: z.string().nullable(),
})
export const jobsPage = z.object({
  jobs: z.array(job),
  triggers: z.array(trigger),
})
export const jobRun = z.object({
  id: z.uuid(),
  jobId: z.uuid(),
  status: z.string(),
  startedAt: z.iso.datetime({ offset: true }),
  finishedAt: z.iso.datetime({ offset: true }).nullable(),
  startedAtDisplay: z.string(),
  durationDisplay: z.string().nullable(),
  shardCount: z.number(),
  succeededShards: z.number(),
  partialShards: z.number(),
  failedShards: z.number(),
})
export const jobRuns = z.array(jobRun)
export const jobRunDetail = z.object({
  id: z.uuid(),
  jobId: z.uuid(),
  status: z.string(),
  startedAt: z.iso.datetime({ offset: true }),
  finishedAt: z.iso.datetime({ offset: true }).nullable(),
  attemptNumber: z.number(),
  originalRunId: z.uuid().nullable(),
  shards: z.array(
    z.object({
      index: z.number(),
      exitCode: z.number(),
      outcome: z.string(),
      artifact: z.string().nullable(),
      log: z.string().nullable(),
    }),
  ),
})
export const jobCodePage = z.object({ job })
export const runJobCodeResult = z.object({ job, run: jobRunDetail })
