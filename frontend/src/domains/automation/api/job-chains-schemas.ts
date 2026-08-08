import { z } from 'zod'
const chainJob = z.object({ id: z.uuid(), name: z.string() })
const gate = z.object({
  type: z.string(),
  durationSeconds: z.number().nullable(),
  expression: z.string().nullable(),
})
const action = z.object({
  type: z.string(),
  displayName: z.string(),
  recipient: z.string().nullable(),
  recipientName: z.string().nullable(),
  subject: z.string().nullable(),
  body: z.string().nullable(),
  attachmentPath: z.string().nullable(),
})
const stage = z.object({
  jobs: z.array(chainJob),
  gate: gate.nullable(),
  action: action.nullable(),
})
export const chain = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  description: z.string().nullable(),
  stages: z.array(stage),
  updatedAt: z.iso.datetime({ offset: true }),
  updatedAtDisplay: z.string(),
})
export const chainsPage = z.object({
  jobs: z.array(chainJob),
  chains: z.array(chain),
  canSendEmail: z.boolean(),
  canSendSms: z.boolean(),
})
const runStep = z.object({
  index: z.number(),
  stageIndex: z.number(),
  branchIndex: z.number(),
  jobId: z.uuid(),
  jobName: z.string(),
  runId: z.uuid().nullable(),
  status: z.string(),
  startedAt: z.iso.datetime({ offset: true }).nullable(),
  finishedAt: z.iso.datetime({ offset: true }).nullable(),
  actionType: z.string().nullable(),
  provider: z.string().nullable(),
  externalId: z.string().nullable(),
  error: z.string().nullable(),
})
export const chainRun = z.object({
  id: z.uuid(),
  chainId: z.uuid(),
  chainName: z.string(),
  status: z.string(),
  steps: z.array(runStep),
  finalOutput: z.string().nullable(),
  startedAt: z.iso.datetime({ offset: true }),
  finishedAt: z.iso.datetime({ offset: true }).nullable(),
  startedAtDisplay: z.string(),
  durationDisplay: z.string().nullable(),
})
export const chainRuns = z.array(chainRun)
