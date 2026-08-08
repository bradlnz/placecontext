import type {
  DataEntity,
  DataMapping,
  ProjectDataAdminModel,
  SaveDataEntityRequest,
  SaveDataMappingRequest,
} from '../model/data-admin'
import { deleteRequest, getJson, postJson } from '../../../shared/api/http-client'
import {
  dataEntitySchema,
  dataMappingSchema,
  projectDataAdminSchema,
  recordLinkRescanSchema,
} from './data-admin-schemas'

const root = (projectId: string) =>
  `/api/v1/projects/${encodeURIComponent(projectId)}/data-admin`

export const fetchProjectDataAdmin = (
  projectId: string,
  signal: AbortSignal,
): Promise<ProjectDataAdminModel> =>
  getJson({ path: root(projectId), schema: projectDataAdminSchema, signal })

export const saveDataMapping = (
  projectId: string,
  body: SaveDataMappingRequest,
  signal: AbortSignal,
): Promise<DataMapping> =>
  postJson({ path: `${root(projectId)}/mappings`, body, schema: dataMappingSchema, signal })

export const deleteDataMapping = (
  projectId: string,
  mappingId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(`${root(projectId)}/mappings/${mappingId}`, signal)

export const saveDataEntity = (
  projectId: string,
  body: SaveDataEntityRequest,
  signal: AbortSignal,
): Promise<DataEntity> =>
  postJson({ path: `${root(projectId)}/entities`, body, schema: dataEntitySchema, signal })

export const deleteDataEntity = (
  projectId: string,
  entityId: string,
  signal: AbortSignal,
): Promise<void> => deleteRequest(`${root(projectId)}/entities/${entityId}`, signal)

export const rescanRecordLinks = (
  projectId: string,
  signal: AbortSignal,
): Promise<{ tablesScanned: number; linksFound: number }> =>
  postJson({
    path: `${root(projectId)}/links/rescan`,
    body: {},
    schema: recordLinkRescanSchema,
    signal,
  })
