import { afterEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'

import { deleteRequest, getJson, putRequest } from './http-client'

describe('HTTP client', () => {
  afterEach(() => vi.restoreAllMocks())

  it('preserves a canonical API error message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ error: 'Role is still assigned.' }), {
        status: 400,
      }),
    )
    await expect(
      getJson({
        path: '/api/test',
        schema: z.object({ ok: z.boolean() }),
        signal: new AbortController().signal,
      }),
    ).rejects.toEqual(
      expect.objectContaining({
        name: 'HttpError',
        status: 400,
        message: 'Role is still assigned.',
      }),
    )
  })

  it('supports no-content PUT and DELETE commands', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(() => Promise.resolve(new Response(null, { status: 204 })))
    const signal = new AbortController().signal
    await putRequest('/api/member', { role: 'Admin' }, signal)
    await deleteRequest('/api/member', signal)
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/member',
      expect.objectContaining({ method: 'PUT' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/member',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
