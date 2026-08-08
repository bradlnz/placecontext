import { z } from 'zod'

import { deleteRequest, getJson, postJson } from '../../../shared/api/http-client'
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

const mcpSettingsSchema: z.ZodType<McpSettings> = z.object({
  projectId: z.uuid().nullable(),
  projects: z.array(z.object({ id: z.uuid(), name: z.string() })),
  connections: z.array(mcpConnectionSchema),
})

const ROOT = '/api/v1/settings/mcp'

export async function fetchMcpSettings(
  projectId: string | undefined,
  signal: AbortSignal,
): Promise<McpSettings> {
  const query = projectId === undefined ? '' : `?projectId=${encodeURIComponent(projectId)}`
  return getJson({
    path: `${ROOT}/context${query}`,
    schema: mcpSettingsSchema,
    signal,
  })
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
