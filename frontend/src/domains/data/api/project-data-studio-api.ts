import type {
  MaterializeProjectDataResult,
  ProjectDataColumnDraft,
  ProjectDataQueryResult,
  ProjectDataRowLink,
  ProjectDataSource,
  ProjectDataStudioModel,
  SavedProjectDataQuery,
} from '../model/project-data-studio'
import { deleteRequest, getJson, postJson, postRequest } from '../../../shared/api/http-client'
import {
  materializeProjectDataResultSchema,
  projectDataQueryResultSchema,
  projectDataRowLinkSchema,
  projectDataStudioSchema,
  savedProjectDataQuerySchema,
} from './project-data-studio-schemas'

const root = (projectId: string) => `/api/v1/projects/${encodeURIComponent(projectId)}/data-studio`

export const fetchProjectDataStudio = (
  projectId: string,
  signal: AbortSignal,
): Promise<ProjectDataStudioModel> =>
  getJson({ path: root(projectId), schema: projectDataStudioSchema, signal })

export const runProjectDataQuery = (
  projectId: string,
  sql: string,
  source: ProjectDataSource,
  signal: AbortSignal,
): Promise<ProjectDataQueryResult> =>
  postJson({
    path: `${root(projectId)}/queries/run`,
    body: { sql, source },
    schema: projectDataQueryResultSchema,
    signal,
  })

export const saveProjectDataQuery = (
  projectId: string,
  name: string,
  sql: string,
  signal: AbortSignal,
): Promise<SavedProjectDataQuery> =>
  postJson({
    path: `${root(projectId)}/saved-queries`,
    body: { name, sql },
    schema: savedProjectDataQuerySchema,
    signal,
  })

export const deleteProjectDataQuery = (
  projectId: string,
  queryId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(`${root(projectId)}/saved-queries/${queryId}`, signal)

export const createProjectDataTable = (
  projectId: string,
  name: string,
  columns: ProjectDataColumnDraft[],
  signal: AbortSignal,
): Promise<void> => postRequest(`${root(projectId)}/tables`, { name, columns }, signal)

export const materializeProjectDataTable = (
  projectId: string,
  tableName: string,
  indexName: string,
  signal: AbortSignal,
): Promise<MaterializeProjectDataResult> =>
  postJson({
    path: `${root(projectId)}/materializations`,
    body: { tableName, indexName },
    schema: materializeProjectDataResultSchema,
    signal,
  })

export const fetchProjectDataRowLinks = (
  projectId: string,
  tableName: string,
  values: Record<string, string | null>,
  signal: AbortSignal,
): Promise<ProjectDataRowLink[]> =>
  postJson({
    path: `${root(projectId)}/row-links`,
    body: { tableName, values },
    schema: projectDataRowLinkSchema.array(),
    signal,
  })
