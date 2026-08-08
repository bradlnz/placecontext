import { describe, expect, it, vi } from 'vitest'

import { fetchIdentityContext } from './identity-api'

describe('fetchIdentityContext', () => {
  it('validates the anonymous form context from the PlaceContext API', async () => {
    const context = {
      configured: false,
      antiforgeryFieldName: '__RequestVerificationToken',
      antiforgeryToken: 'verification-token',
    }
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(
          new Response(JSON.stringify(context), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        ),
      ),
    )

    await expect(fetchIdentityContext(new AbortController().signal)).resolves.toEqual(context)
  })
})
