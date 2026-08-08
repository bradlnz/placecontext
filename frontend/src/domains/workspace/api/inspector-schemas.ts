import { z } from 'zod'

export const inspectorToolCallsSchema = z.array(
  z.object({
    id: z.string().min(1),
    tool: z.string(),
    direction: z.string(),
    project: z.string(),
    summary: z.string(),
    status: z.string(),
    durationMs: z.number().int().nonnegative(),
    requestJson: z.string(),
    responseJson: z.string(),
    at: z.iso.datetime({ offset: true }),
  }),
)
