import { z } from 'zod'

export const openSearchIndexSchema = z.object({
  name: z.string(),
  documentCount: z.number(),
  storeSize: z.string().nullable(),
})

export const openSearchFieldSchema = z.object({
  name: z.string(),
  type: z.string(),
  searchable: z.boolean(),
  aggregatable: z.boolean(),
})

export const openSearchDashboardSchema = z.object({
  id: z.string(),
  projectId: z.string(),
  name: z.string(),
  indexPattern: z.string(),
  queryText: z.string().nullable(),
  bucketField: z.string(),
  bucketType: z.string(),
  chartType: z.string(),
  metricType: z.string(),
  metricField: z.string().nullable(),
  dateInterval: z.string().nullable(),
  chartSpecJson: z.string(),
  createdAt: z.string(),
  updatedAt: z.string(),
})

export const openSearchPageSchema = z.object({
  indices: z.array(openSearchIndexSchema),
  dashboards: z.array(openSearchDashboardSchema),
  selectedIndex: z.string(),
  fields: z.array(openSearchFieldSchema),
  lastUpdated: z.object({ value: z.string().nullable(), field: z.string().nullable() }).nullable(),
  canSync: z.boolean(),
  error: z.string().nullable(),
})

export const openSearchHitSchema = z.object({
  index: z.string(),
  id: z.string(),
  score: z.number().nullable(),
  fields: z.record(z.string(), z.string().nullable()),
})

export const openSearchResultSchema = z.object({
  total: z.number(),
  tookMs: z.number(),
  hits: z.array(openSearchHitSchema),
  chartSpecJson: z.string().nullable(),
})

export const openSearchSyncSchema = z.object({
  accepted: z.boolean(),
  status: z.string(),
  message: z.string(),
})
