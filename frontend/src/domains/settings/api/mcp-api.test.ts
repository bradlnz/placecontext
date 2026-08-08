import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  createMcpConnection,
  deleteMcpConnection,
  fetchMcpSettings,
  testMcpConnection,
} from './mcp-api'

const connection = {
  id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
  projectId: 'a102ed75-e94a-48fe-9826-2532d524857f',
  name: 'Tools',
  transport: 'http',
  endpointUrl: 'https://mcp.example.com/mcp',
  command: null,
  args: null,
  authType: 'oauth',
  enabled: true,
  lastStatus: null,
  lastConnectedAt: null,
  createdAt: '2026-08-08T00:00:00Z',
  oAuthTokenExpiresAt: null,
  oAuthClientId: null,
  oAuthScopes: 'openid',
}

describe('MCP settings API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the composed project context from api/v1', async () => {
    const body = {
      projectId: connection.projectId,
      projects: [{ id: connection.projectId, name: 'Atlas' }],
      connections: [connection],
    }
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))
    await expect(
      fetchMcpSettings(connection.projectId, new AbortController().signal),
    ).resolves.toEqual(body)
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/v1/settings/mcp/context?projectId=${connection.projectId}`,
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('creates, tests, and deletes connections through canonical routes', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(() =>
        Promise.resolve(new Response(JSON.stringify(connection), { status: 200 })),
      )
    const signal = new AbortController().signal
    await createMcpConnection(
      connection.projectId,
      {
        name: 'Tools',
        transport: 'http',
        endpointUrl: connection.endpointUrl,
        command: null,
        args: null,
        authType: 'none',
        authToken: null,
        authHeader: null,
        oAuthScopes: null,
      },
      signal,
    )
    await testMcpConnection(connection.id, signal)
    await deleteMcpConnection(connection.id, signal)
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/settings/mcp/projects/${connection.projectId}/connections`,
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/settings/mcp/connections/${connection.id}/test`,
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/settings/mcp/connections/${connection.id}`,
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
