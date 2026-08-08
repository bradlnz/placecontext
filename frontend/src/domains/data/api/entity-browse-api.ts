import type { EntityBrowseModel, RecordLink } from '../model/entity-browse'
import { getJson, postJson } from '../../../shared/api/http-client'
import {
  entityBrowseSchema,
  recordCreateResultSchema,
  recordLinkSchema,
  recordWriteResultSchema,
} from './entity-browse-schemas'

const root = (projectId: string, entityName: string) =>
  `/api/v1/projects/${encodeURIComponent(projectId)}/entity-page/${encodeURIComponent(entityName)}`

export const fetchEntityBrowse = (
  projectId: string,
  entityName: string,
  search: string,
  page: number,
  signal: AbortSignal,
): Promise<EntityBrowseModel> => {
  const params = new URLSearchParams({ search, page: String(page), pageSize: '50' })
  return getJson({
    path: `${root(projectId, entityName)}?${params.toString()}`,
    schema: entityBrowseSchema,
    signal,
  })
}

export const createEntityRecord = (
  projectId: string,
  entityName: string,
  values: Record<string, string | null>,
  signal: AbortSignal,
): Promise<{ duplicateWarnings: string[] }> =>
  postJson({
    path: `${root(projectId, entityName)}/records/create`,
    body: { values },
    schema: recordCreateResultSchema,
    signal,
  })

export const updateEntityRecord = (
  projectId: string,
  entityName: string,
  keys: Record<string, string | null>,
  values: Record<string, string | null>,
  signal: AbortSignal,
): Promise<{ affected: number }> =>
  postJson({
    path: `${root(projectId, entityName)}/records/update`,
    body: { keys, values },
    schema: recordWriteResultSchema,
    signal,
  })

export const deleteEntityRecord = (
  projectId: string,
  entityName: string,
  keys: Record<string, string | null>,
  signal: AbortSignal,
): Promise<{ affected: number }> =>
  postJson({
    path: `${root(projectId, entityName)}/records/delete`,
    body: { keys },
    schema: recordWriteResultSchema,
    signal,
  })

export const fetchEntityRecordLinks = (
  projectId: string,
  entityName: string,
  values: Record<string, string | null>,
  signal: AbortSignal,
): Promise<RecordLink[]> =>
  postJson({
    path: `${root(projectId, entityName)}/records/links`,
    body: { values },
    schema: recordLinkSchema.array(),
    signal,
  })
