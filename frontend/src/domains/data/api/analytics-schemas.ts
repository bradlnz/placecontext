import { z } from 'zod'

export const analyticsChartSchema = z.object({
  tableName: z.string(),
  name: z.string(),
  generatedAt: z.iso.datetime({ offset: true }),
  generatedAtDisplay: z.string(),
  spec: z.record(z.string(), z.unknown()).nullable(),
  legacyHtml: z.string().nullable(),
  sql: z.string().nullable(),
  chartType: z.string(),
})
export const analyticsPageSchema = z.object({
  tables: z.array(z.object({ name: z.string(), rowEstimate: z.number().int().nonnegative() })),
  charts: z.array(analyticsChartSchema),
  sweepPending: z.boolean(),
  pendingTables: z.array(z.string()),
})
export const analyticsMessageSchema = z.object({ message: z.string() })
