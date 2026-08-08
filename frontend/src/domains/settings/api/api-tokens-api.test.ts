import { afterEach, describe, expect, it, vi } from 'vitest'

import { createApiToken, fetchApiTokens, revokeApiToken } from './api-tokens-api'

const token = {
  id: '7b4eed95-c20a-4522-86f4-2f9ae5891302',
  name: 'CI',
  tokenPrefix: 'pc_123456',
  createdAt: '2026-08-08T00:00:00+00:00',
  lastUsedAt: null,
  expiresAt: null,
}

describe('API tokens API', () => {
  afterEach(() => vi.restoreAllMocks())
  it('uses canonical api/v1 routes for list, create, and revoke', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify([token]), { status: 200 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ ...token, rawToken: 'pc_secret' }), { status: 200 }),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ revoked: true }), { status: 200 }))
    const signal = new AbortController().signal
    await fetchApiTokens(signal)
    await createApiToken('CI', 90, signal)
    await revokeApiToken(token.id, signal)
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/settings/api-tokens',
      expect.objectContaining({ method: 'GET' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/settings/api-tokens',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/settings/api-tokens/${token.id}`,
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
