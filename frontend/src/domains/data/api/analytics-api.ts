import { deleteRequest, getJson, postJson, putJson } from '../../../shared/api/http-client'
import type { AnalyticsChart, AnalyticsPageModel } from '../model/analytics'
import {
  analyticsChartSchema,
  analyticsMessageSchema,
  analyticsPageSchema,
} from './analytics-schemas'

const path = (projectId: string) => `/api/v1/projects/${encodeURIComponent(projectId)}/analytics`
export const fetchAnalytics = (
  projectId: string,
  signal: AbortSignal,
): Promise<AnalyticsPageModel> =>
  getJson({ path: path(projectId), schema: analyticsPageSchema, signal })
export async function queueAnalyticsRefresh(
  projectId: string,
  tableName: string | null,
  instruction: string,
  signal: AbortSignal,
): Promise<string> {
  return (
    await postJson({
      path: `${path(projectId)}/refreshes`,
      body: { tableName, instruction },
      schema: analyticsMessageSchema,
      signal,
    })
  ).message
}
export const saveSqlChart = (
  projectId: string,
  name: string,
  sql: string,
  chartType: string,
  signal: AbortSignal,
): Promise<AnalyticsChart> =>
  putJson({
    path: `${path(projectId)}/sql-charts`,
    body: { name, sql, chartType },
    schema: analyticsChartSchema,
    signal,
  })
export const deleteSqlChart = (
  projectId: string,
  name: string,
  signal: AbortSignal,
): Promise<void> =>
  deleteRequest(`${path(projectId)}/sql-charts/${encodeURIComponent(name)}`, signal)
