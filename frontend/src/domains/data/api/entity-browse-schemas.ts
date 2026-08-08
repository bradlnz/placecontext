import { z } from 'zod'
import { dataEntitySchema } from './data-admin-schemas'

export const entityBrowseSchema = z.object({
  entity: dataEntitySchema,
  columns: z.array(
    z.object({
      name: z.string(),
      type: z.string(),
      notNull: z.boolean(),
      primaryKey: z.boolean(),
    }),
  ),
  page: z.object({
    columns: z.array(z.string()),
    rows: z.array(z.array(z.string().nullable())),
    totalCount: z.number(),
    page: z.number(),
    pageSize: z.number(),
  }),
})

export const recordWriteResultSchema = z.object({ affected: z.number() })
export const recordCreateResultSchema = z.object({ duplicateWarnings: z.array(z.string()) })
export const recordLinkSchema = z.object({
  projectId: z.string(),
  kind: z.string(),
  normalizedValue: z.string(),
  displayValue: z.string(),
  tableName: z.string(),
  columnName: z.string(),
  rowKey: z.string(),
})
