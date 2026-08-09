import {
  deleteJsonWithBody,
  deleteRequest,
  getJson,
  HttpError,
  postJson,
} from '../../../shared/api/http-client'
import type {
  ArtifactFile,
  ArtifactShareCreated,
  ArtifactShareStatus,
  ArtifactsPageModel,
} from '../model/artifacts'
import {
  artifactCapabilitiesSchema,
  artifactFileSchema,
  artifactShareCreatedSchema,
  artifactShareStatusSchema,
  deleteArtifactsResultSchema,
} from './artifacts-schemas'
import { fetchArtifactFilters } from '../../settings/api/settings-api'
import { fetchWorkspaceProjects } from '../../workspace/api/fetch-workspace-overview'

const ROOT = '/api/artifacts'

export function artifactDownloadUrl(artifact: ArtifactFile): string {
  const version = Math.floor(new Date(artifact.createdAt).getTime() / 1000)
  return `/runs/${encodeURIComponent(artifact.runId)}/artifacts/${encodeURIComponent(artifact.id)}?v=${String(version)}`
}

export const fetchArtifactsPage = (
  projectId: string,
  search: string,
  signal: AbortSignal,
): Promise<ArtifactsPageModel> => {
  const params = new URLSearchParams()
  params.set('take', projectId === '' ? '1000' : '2000')
  if (search.trim() !== '') params.set('search', search.trim())
  const filesPath =
    projectId === ''
      ? `${ROOT}/recent?${params.toString()}`
      : `${ROOT}/projects/${encodeURIComponent(projectId)}?${params.toString()}`

  return Promise.all([
    getJson({ path: filesPath, schema: artifactFileSchema.array(), signal }),
    getJson({ path: `${ROOT}/capabilities`, schema: artifactCapabilitiesSchema, signal }),
    fetchWorkspaceProjects(signal),
    fetchArtifactFilters(signal),
  ]).then(([files, capabilities, projects, config]) => ({
    files,
    projects: projects.map(({ id, name }) => ({ id, name })),
    config,
    ...capabilities,
    loadMayBeIncomplete: files.length >= (projectId === '' ? 1000 : 2000),
  }))
}

export const deleteArtifacts = (
  artifactIds: string[],
  signal: AbortSignal,
): Promise<{ deleted: number }> =>
  deleteJsonWithBody({
    path: ROOT,
    body: { artifactIds },
    schema: deleteArtifactsResultSchema,
    signal,
  })

export const fetchArtifactShareStatus = (
  artifactId: string,
  signal: AbortSignal,
): Promise<ArtifactShareStatus | null> =>
  getJson({
    path: `${ROOT}/${encodeURIComponent(artifactId)}/share`,
    schema: artifactShareStatusSchema.nullable(),
    signal,
  })

export const createArtifactShare = (
  artifactId: string,
  lifetimeDays: number,
  signal: AbortSignal,
): Promise<ArtifactShareCreated> =>
  postJson({
    path: `${ROOT}/${encodeURIComponent(artifactId)}/share`,
    body: { lifetimeDays },
    schema: artifactShareCreatedSchema,
    signal,
  })

export const revokeArtifactShare = (artifactId: string, signal: AbortSignal): Promise<void> =>
  deleteRequest(`${ROOT}/${encodeURIComponent(artifactId)}/share`, signal)

export async function fetchArtifactText(
  artifact: ArtifactFile,
  signal: AbortSignal,
): Promise<string> {
  const response = await fetch(artifactDownloadUrl(artifact), {
    credentials: 'same-origin',
    headers: { Accept: artifact.contentType },
    signal,
  })
  if (!response.ok) throw new HttpError(response.status, 'Could not load the artifact preview.')
  return response.text()
}
