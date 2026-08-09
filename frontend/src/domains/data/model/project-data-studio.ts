import type { RecordLink } from './entity-browse'

export type ProjectDataSource = 'postgres' | 'opensearch'

export interface ProjectDataTable {
  name: string
  rowEstimate: number
  readOnly: boolean
  isView: boolean
}

export interface ProjectDataIndex {
  name: string
  documentCount: number
  storeSize: string | null
}

export interface SavedProjectDataQuery {
  id: string
  projectId: string
  name: string
  sql: string
  createdAt: string
  updatedAt: string
}

export interface ProjectDataStudioModel {
  tables: ProjectDataTable[]
  indices: ProjectDataIndex[]
  savedQueries: SavedProjectDataQuery[]
}

export interface ProjectDataQueryResult {
  columns: string[]
  rows: (string | null)[][]
  affectedRows: number
  truncated: boolean
}

export interface ProjectDataColumnDraft {
  name: string
  type: string
  notNull: boolean
  primaryKey: boolean
}

export interface MaterializeProjectDataResult {
  indexName: string
  rowsIndexed: number
  columnCount: number
  truncated: boolean
  sourceTable: string
}

export interface ProjectDataTab {
  key: string
  name: string
  source: ProjectDataSource
  sql: string
  result: ProjectDataQueryResult | null
  error: string | null
  running: boolean
}

export type ProjectDataRowLink = RecordLink
