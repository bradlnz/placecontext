import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchCommunications } from './communications-api'

describe('communications API', () => {
  afterEach(() => vi.restoreAllMocks())
  it('loads its context from the canonical api/v1 route', async () => {
    const body = { providers: [], projects: [] }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))
    await expect(fetchCommunications(new AbortController().signal)).resolves.toEqual(body)
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/settings/communications/context', expect.objectContaining({ method: 'GET' }))
  })
})
