export interface OpenSearchIndex {
  name: string
  documentCount: number
  storeSize: string | null
}

export interface OpenSearchField {
  name: string
  type: string
  searchable: boolean
  aggregatable: boolean
}

export interface OpenSearchDashboard {
  id: string
  projectId: string
  name: string
  indexPattern: string
  queryText: string | null
  bucketField: string
  bucketType: string
  chartType: string
  metricType: string
  metricField: string | null
  dateInterval: string | null
  chartSpecJson: string
  createdAt: string
  updatedAt: string
}

export interface OpenSearchPageModel {
  indices: OpenSearchIndex[]
  dashboards: OpenSearchDashboard[]
  selectedIndex: string
  fields: OpenSearchField[]
  lastUpdated: { value: string | null; field: string | null } | null
  canSync: boolean
  error: string | null
}

export interface OpenSearchHit {
  index: string
  id: string
  score: number | null
  fields: Record<string, string | null>
}

export interface OpenSearchResult {
  total: number
  tookMs: number
  hits: OpenSearchHit[]
  chartSpecJson: string | null
}

export interface OpenSearchRequest {
  indexPattern: string
  queryText: string | null
  page: number
  pageSize: number
  bucketField: string | null
  bucketType: string
  chartType: string
  metricType: string
  metricField: string | null
  dateInterval: string | null
}

export interface SaveOpenSearchDashboardRequest extends Omit<
  OpenSearchDashboard,
  'id' | 'projectId' | 'createdAt' | 'updatedAt'
> {
  dashboardId: string | null
}

export interface GeneratedOpenSearchChart {
  id: string
  title: string
  subtitle: string
  chartSpecJson: string
}
