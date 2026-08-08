export interface DataField {
  sourcePath: string
  column: string
  type: string
}

export interface DataMapping {
  id: string
  projectId: string
  jobId: string
  jobName: string
  sourceKind: string
  targetTable: string
  rowsPath: string | null
  fields: DataField[]
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export interface DataAdminJob {
  id: string
  name: string
  returnType: string
}

export interface ProjectTable {
  name: string
  rowEstimate: number
  readOnly: boolean
  isView: boolean
}

export interface EntityRelation {
  column: string
  targetEntity: string
  targetColumn: string
}

export interface DataEntity {
  id: string
  projectId: string
  name: string
  tableName: string
  labelColumn: string | null
  relations: EntityRelation[]
  tags: string[]
  updatedAt: string
}

export interface RecordLinkOccurrence {
  projectId: string
  kind: string
  normalizedValue: string
  displayValue: string
  tableName: string
  columnName: string
  rowKey: string
}

export interface RecordLinkGroup {
  kind: string
  normalizedValue: string
  displayValue: string
  occurrences: RecordLinkOccurrence[]
}

export interface ProjectDataAdminModel {
  mappings: DataMapping[]
  jobs: DataAdminJob[]
  tables: ProjectTable[]
  entities: DataEntity[]
  linkGroups: RecordLinkGroup[]
}

export interface SaveDataMappingRequest {
  id: string | null
  jobId: string
  targetTable: string
  rowsPath: string | null
  fields: DataField[]
  enabled: boolean
}

export interface SaveDataEntityRequest {
  id: string | null
  name: string
  tableName: string
  labelColumn: string | null
  relations: EntityRelation[]
  tags: string[]
}
