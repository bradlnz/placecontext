import { z } from 'zod'

const nonnegativeInteger = z.number().int().nonnegative()

export const dashboardSchema = z.object({
  project: z
    .object({
      id: z.uuid(),
      name: z.string(),
    })
    .nullable(),
  stats: z.object({
    running: nonnegativeInteger,
    queued: nonnegativeInteger,
    failed24: nonnegativeInteger,
    succeeded24: nonnegativeInteger,
  }),
  chains: z.array(
    z.object({
      id: z.uuid(),
      projectId: z.uuid(),
      name: z.string(),
      stageCount: nonnegativeInteger,
      jobCount: nonnegativeInteger,
      promptSteps: z.array(
        z.object({
          index: nonnegativeInteger,
          jobName: z.string(),
          parameters: z.array(
            z.object({
              name: z.string(),
              label: z.string(),
              required: z.boolean(),
              type: z.string(),
              options: z.array(z.string()),
              defaultValue: z.string(),
            }),
          ),
        }),
      ),
    }),
  ),
  entities: z.array(
    z.object({
      id: z.uuid(),
      projectId: z.uuid(),
      name: z.string(),
      tableName: z.string(),
      rowCount: nonnegativeInteger.nullable(),
      chartColumn: z.string().nullable(),
      bars: z.array(
        z.object({
          label: z.string(),
          count: nonnegativeInteger,
          percentage: z.number().int().min(0).max(100),
        }),
      ),
    }),
  ),
  charts: z.array(
    z.object({
      name: z.string(),
      spec: z.record(z.string(), z.unknown()),
      generatedAt: z.string(),
    }),
  ),
  recentRuns: z.array(
    z.object({
      id: z.uuid(),
      jobName: z.string(),
      projectName: z.string(),
      status: z.string(),
      succeededShards: nonnegativeInteger,
      failedShards: nonnegativeInteger,
      startedAt: z.string(),
      finishedAt: z.string().nullable(),
      sourceKind: z.string(),
    }),
  ),
})

export const runDashboardChainResultSchema = z.object({
  chainRunId: z.uuid(),
  message: z.string(),
})
