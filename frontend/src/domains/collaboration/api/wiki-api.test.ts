import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchWikiContext } from './wiki-api'

const context = {
  articles: [
    {
      slug: 'getting-started',
      title: 'Getting started',
      summary: 'Start here.',
    },
  ],
  article: {
    slug: 'getting-started',
    title: 'Getting started',
    summary: 'Start here.',
    html: '<h1>Getting started</h1>',
  },
}

describe('Wiki API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the default article and a selected slug', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(() =>
        Promise.resolve(new Response(JSON.stringify(context), { status: 200 })),
      )
    const signal = new AbortController().signal

    await expect(fetchWikiContext(undefined, signal)).resolves.toEqual(context)
    await expect(fetchWikiContext('getting started', signal)).resolves.toEqual(context)
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/wiki',
      expect.objectContaining({ method: 'GET' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/wiki?slug=getting%20started',
      expect.objectContaining({ method: 'GET' }),
    )
  })
})
