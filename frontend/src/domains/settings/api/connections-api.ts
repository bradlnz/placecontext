import { z } from 'zod'

import { deleteJson, getJson, putJson } from '../../../shared/api/http-client'
import type { ConnectionProject, ConnectionsSettings, ExternalDatabaseInput, ExternalIndexInput } from '../model/connections'

const connectionProjectSchema: z.ZodType<ConnectionProject> = z.object({
  id: z.uuid(),
  name: z.string(),
  hasExternalDatabase: z.boolean(),
  hasExternalIndex: z.boolean(),
})

const connectionsSettingsSchema: z.ZodType<ConnectionsSettings> = z.object({
  projects: z.array(connectionProjectSchema),
  sslModes: z.array(z.string()),
})

const ROOT = '/api/v1/settings/connections'

export async function fetchConnections(signal: AbortSignal): Promise<ConnectionsSettings> {
  return getJson({ path: `${ROOT}/context`, schema: connectionsSettingsSchema, signal })
}

export async function saveExternalDatabase(projectId: string, input: ExternalDatabaseInput, signal: AbortSignal): Promise<ConnectionProject> {
  return putJson({ path: `${ROOT}/projects/${projectId}/database`, body: input, schema: connectionProjectSchema, signal })
}

export async function resetExternalDatabase(projectId: string, signal: AbortSignal): Promise<ConnectionProject> {
  return deleteJson({ path: `${ROOT}/projects/${projectId}/database`, schema: connectionProjectSchema, signal })
}

export async function saveExternalIndex(projectId: string, input: ExternalIndexInput, signal: AbortSignal): Promise<ConnectionProject> {
  return putJson({ path: `${ROOT}/projects/${projectId}/index`, body: input, schema: connectionProjectSchema, signal })
}

export async function resetExternalIndex(projectId: string, signal: AbortSignal): Promise<ConnectionProject> {
  return deleteJson({ path: `${ROOT}/projects/${projectId}/index`, schema: connectionProjectSchema, signal })
}
