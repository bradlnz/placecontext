import { z } from 'zod'

import { deleteRequest, getJson, HttpError, postJson } from '../../../shared/api/http-client'
import { fetchWorkspaceProjects } from '../../workspace/api/fetch-workspace-overview'
import type { CreateMcpConnectionInput, McpConnection, McpSettings } from '../model/mcp'

const mcpConnectionSchema: z.ZodType<McpConnection> = z.object({
  id: z.uuid(),
  projectId: z.uuid(),
  name: z.string(),
  transport: z.enum(['http', 'sse', 'stdio']),
  endpointUrl: z.string().nullable(),
  command: z.string().nullable(),
  args: z.string().nullable(),
  authType: z.enum(['none', 'bearer', 'apikey', 'header', 'oauth']).nullable(),
  enabled: z.boolean(),
  lastStatus: z.string().nullable(),
  lastConnectedAt: z.string().nullable(),
  createdAt: z.string(),
  oAuthTokenExpiresAt: z.string().nullable(),
  oAuthClientId: z.string().nullable(),
  oAuthScopes: z.string().nullable(),
})

const ROOT = '/api/mcp'

export async function fetchMcpSettings(
  projectId: string | undefined,
  signal: AbortSignal,
): Promise<McpSettings> {
  const projects = await fetchWorkspaceProjects(signal)
  const selectedProjectId = projectId ?? projects[0]?.id ?? null
  if (selectedProjectId !== null && !projects.some((project) => project.id === selectedProjectId)) {
    throw new HttpError(404, 'Project not found.')
  }

  const connections =
    selectedProjectId === null
      ? []
      : await getJson({
          path: `${ROOT}/projects/${encodeURIComponent(selectedProjectId)}/connections`,
          schema: mcpConnectionSchema.array(),
          signal,
        })

  return {
    projectId: selectedProjectId,
    projects: projects.map(({ id, name }) => ({ id, name })),
    connections,
  }
}

export async function createMcpConnection(
  projectId: string,
  input: CreateMcpConnectionInput,
  signal: AbortSignal,
): Promise<McpConnection> {
  return postJson({
    path: `${ROOT}/projects/${projectId}/connections`,
    body: input,
    schema: mcpConnectionSchema,
    signal,
  })
}

export async function testMcpConnection(id: string, signal: AbortSignal): Promise<McpConnection> {
  return postJson({
    path: `${ROOT}/connections/${id}/test`,
    body: {},
    schema: mcpConnectionSchema,
    signal,
  })
}

export async function deleteMcpConnection(id: string, signal: AbortSignal): Promise<void> {
  await deleteRequest(`${ROOT}/connections/${id}`, signal)
}
