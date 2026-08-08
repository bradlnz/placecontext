import { z } from 'zod'

export const dataFieldSchema = z.object({
  sourcePath: z.string(),
  column: z.string(),
  type: z.string(),
})

export const dataMappingSchema = z.object({
  id: z.string(),
  projectId: z.string(),
  jobId: z.string(),
  jobName: z.string(),
  sourceKind: z.string(),
  targetTable: z.string(),
  rowsPath: z.string().nullable(),
  fields: z.array(dataFieldSchema),
  enabled: z.boolean(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const dataEntitySchema = z.object({
  id: z.string(),
  projectId: z.string(),
  name: z.string(),
  tableName: z.string(),
  labelColumn: z.string().nullable(),
  relations: z.array(
    z.object({
      column: z.string(),
      targetEntity: z.string(),
      targetColumn: z.string(),
    }),
  ),
  tags: z.array(z.string()),
  updatedAt: z.string(),
})

export const projectDataAdminSchema = z.object({
  mappings: z.array(dataMappingSchema),
  jobs: z.array(z.object({ id: z.string(), name: z.string(), returnType: z.string() })),
  tables: z.array(
    z.object({
      name: z.string(),
      rowEstimate: z.number(),
      readOnly: z.boolean(),
      isView: z.boolean(),
    }),
  ),
  entities: z.array(dataEntitySchema),
  linkGroups: z.array(
    z.object({
      kind: z.string(),
      normalizedValue: z.string(),
      displayValue: z.string(),
      occurrences: z.array(
        z.object({
          projectId: z.string(),
          kind: z.string(),
          normalizedValue: z.string(),
          displayValue: z.string(),
          tableName: z.string(),
          columnName: z.string(),
          rowKey: z.string(),
        }),
      ),
    }),
  ),
})

export const recordLinkRescanSchema = z.object({
  tablesScanned: z.number(),
  linksFound: z.number(),
})
