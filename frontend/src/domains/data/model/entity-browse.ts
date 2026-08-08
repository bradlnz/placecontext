import type { DataEntity } from './data-admin'

export interface ProjectColumn {
  name: string
  type: string
  notNull: boolean
  primaryKey: boolean
}

export interface ProjectTablePage {
  columns: string[]
  rows: Array<Array<string | null>>
  totalCount: number
  page: number
  pageSize: number
}

export interface EntityBrowseModel {
  entity: DataEntity
  columns: ProjectColumn[]
  page: ProjectTablePage
}

export interface RecordLink {
  projectId: string
  kind: string
  normalizedValue: string
  displayValue: string
  tableName: string
  columnName: string
  rowKey: string
}
