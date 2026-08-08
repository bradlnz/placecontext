import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchInspectorToolCalls } from './inspector-api'

describe('Inspector API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads and validates the recent MCP tool-call feed', async () => {
    const calls = [
      {
        id: 'call-1',
        tool: 'search_context',
        direction: 'inbound',
        project: 'Atlas',
        summary: 'Search project context',
        status: 'Ok',
        durationMs: 42,
        requestJson: '{"query":"roads"}',
        responseJson: '{"count":3}',
        at: '2026-08-08T00:00:00+00:00',
      },
    ]
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify(calls), { status: 200 }))

    await expect(fetchInspectorToolCalls(new AbortController().signal)).resolves.toEqual(calls)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/inspector/tool-calls?take=20',
      expect.objectContaining({ method: 'GET' }),
    )
  })
})
