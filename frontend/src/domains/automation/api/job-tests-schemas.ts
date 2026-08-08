import { z } from 'zod'

const codeFile = z.object({ path: z.string(), content: z.string() })
const method = z.object({
  name: z.string(),
  status: z.string(),
  durationMs: z.number().nullable(),
  message: z.string().nullable(),
})
export const jobTestBlock = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  jobId: z.uuid(),
  jobName: z.string(),
  name: z.string(),
  inputPayload: z.string().nullable(),
  assertionType: z.enum(['Succeeds', 'OutputEquals', 'OutputContains', 'JsonSubset']),
  expectedValue: z.string().nullable(),
  enabled: z.boolean(),
  lastStatus: z.string(),
  lastMessage: z.string().nullable(),
  lastActualOutput: z.string().nullable(),
  lastDurationMs: z.number().nullable(),
  runtimeId: z.string(),
  runtimeLabel: z.string(),
  entrypoint: z.string().nullable(),
  codeFiles: z.array(codeFile),
  methodResults: z.array(method),
})
export const jobTestsPage = z.object({
  jobs: z.array(z.object({ id: z.uuid(), name: z.string() })),
  tests: z.array(jobTestBlock),
})
export const jobTestCodePage = z.object({
  test: jobTestBlock,
  runtimes: z.array(
    z.object({
      id: z.string(),
      label: z.string(),
      frameworkLabel: z.string(),
      entrypoint: z.string(),
      starterFiles: z.array(codeFile),
    }),
  ),
})
