export interface AnalyticsTable {
  name: string
  rowEstimate: number
}
export interface AnalyticsChart {
  tableName: string
  name: string
  generatedAt: string
  generatedAtDisplay: string
  spec: Record<string, unknown> | null
  legacyHtml: string | null
  sql: string | null
  chartType: string
}
export interface AnalyticsPageModel {
  tables: AnalyticsTable[]
  charts: AnalyticsChart[]
  sweepPending: boolean
  pendingTables: string[]
}
