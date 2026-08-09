import { z } from 'zod'
const target = z.object({ id: z.uuid(), name: z.string() })
export const trigger = z.object({
  id: z.uuid(),
  name: z.string(),
  kind: z.string(),
  enabled: z.boolean(),
  cronExpression: z.string().nullable(),
  eventName: z.string().nullable(),
  jobId: z.uuid().nullable(),
  chainId: z.uuid().nullable(),
  sourceTable: z.string().nullable(),
  prompt: z.string().nullable(),
  targetLabel: z.string(),
  nextRunLabel: z.string(),
  lastFiredLabel: z.string(),
})
export const schedulePage = z.object({
  timeZoneId: z.string(),
  jobs: z.array(target),
  chains: z.array(target),
  tables: z.array(z.string()),
  eventTypes: z.array(z.string()),
  triggers: z.array(trigger),
})

export const scheduleServicePage = schedulePage.omit({ tables: true }).extend({
  tables: z.array(z.string()),
})

export const scheduleDataTables = z.array(
  z.looseObject({ name: z.string() }),
)
