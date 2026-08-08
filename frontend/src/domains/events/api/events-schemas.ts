import { z } from 'zod'

const eventType = z.object({
  name: z.string(),
  description: z.string().nullable(),
  isBuiltIn: z.boolean(),
  payloadSchema: z.string().nullable(),
})
const occurrence = z.object({
  id: z.uuid(),
  name: z.string(),
  source: z.string(),
  sourceLabel: z.string(),
  payload: z.string().nullable(),
  occurredAt: z.iso.datetime({ offset: true }),
  occurredAtDisplay: z.string(),
  triggeredRuns: z.number(),
})
const trigger = z.object({ id: z.uuid(), eventName: z.string().nullable(), enabled: z.boolean() })
export const eventsPage = z.object({
  types: z.array(eventType),
  log: z.array(occurrence),
  triggers: z.array(trigger),
})
export const emittedEvent = z.object({ triggeredRuns: z.number() })
