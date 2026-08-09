import type {
  OpenSearchDashboard,
  OpenSearchPageModel,
  OpenSearchRequest,
  OpenSearchResult,
  SaveOpenSearchDashboardRequest,
} from '../model/open-search'
import { deleteRequest, getJson, postJson } from '../../../shared/api/http-client'
import {
  openSearchDashboardSchema,
  openSearchPageSchema,
  openSearchResultSchema,
  openSearchSyncSchema,
} from './open-search-schemas'

const root = (projectId: string) => `/api/v1/projects/${encodeURIComponent(projectId)}/opensearch`

export const fetchOpenSearchPage = (
  projectId: string,
  index: string,
  signal: AbortSignal,
): Promise<OpenSearchPageModel> => {
  const search = index === '' ? '' : `?index=${encodeURIComponent(index)}`
  return getJson({ path: `${root(projectId)}/page${search}`, schema: openSearchPageSchema, signal })
}

export const searchOpenSearch = (
  projectId: string,
  request: OpenSearchRequest,
  signal: AbortSignal,
): Promise<OpenSearchResult> =>
  postJson({
    path: `${root(projectId)}/search`,
    body: request,
    schema: openSearchResultSchema,
    signal,
  })

export const saveOpenSearchDashboard = (
  projectId: string,
  request: SaveOpenSearchDashboardRequest,
  signal: AbortSignal,
): Promise<OpenSearchDashboard> =>
  postJson({
    path: `${root(projectId)}/dashboards`,
    body: request,
    schema: openSearchDashboardSchema,
    signal,
  })

export const deleteOpenSearchDashboard = (
  projectId: string,
  dashboardId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(`${root(projectId)}/dashboards/${dashboardId}`, signal)

export const triggerOpenSearchSync = (
  projectId: string,
  signal: AbortSignal,
): Promise<{ accepted: boolean; status: string; message: string }> =>
  postJson({ path: `${root(projectId)}/sync`, body: {}, schema: openSearchSyncSchema, signal })
