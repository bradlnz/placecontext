import { z } from 'zod'

export const savedProjectDataQuerySchema = z.object({
  id: z.string(),
  projectId: z.string(),
  name: z.string(),
  sql: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const projectDataStudioSchema = z.object({
  tables: z.array(
    z.object({
      name: z.string(),
      rowEstimate: z.number(),
      readOnly: z.boolean(),
      isView: z.boolean(),
    }),
  ),
  indices: z.array(
    z.object({
      name: z.string(),
      documentCount: z.number(),
      storeSize: z.string().nullable(),
    }),
  ),
  savedQueries: z.array(savedProjectDataQuerySchema),
})

export const projectDataQueryResultSchema = z.object({
  columns: z.array(z.string()),
  rows: z.array(z.array(z.string().nullable())),
  affectedRows: z.number(),
  truncated: z.boolean(),
})

export const materializeProjectDataResultSchema = z.object({
  indexName: z.string(),
  rowsIndexed: z.number(),
  columnCount: z.number(),
  truncated: z.boolean(),
  sourceTable: z.string(),
})

export const projectDataRowLinkSchema = z.object({
  projectId: z.string(),
  kind: z.string(),
  normalizedValue: z.string(),
  displayValue: z.string(),
  tableName: z.string(),
  columnName: z.string(),
  rowKey: z.string(),
})
