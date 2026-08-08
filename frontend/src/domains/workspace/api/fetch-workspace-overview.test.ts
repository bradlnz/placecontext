import { describe, expect, it, vi } from 'vitest'

import { workspaceOverviewFixture } from '../../../test/fixtures/workspace'
import { fetchWorkspaceOverview } from './fetch-workspace-overview'

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('fetchWorkspaceOverview', () => {
  it('starts each independent workspace request before awaiting responses', async () => {
    const resolvers: ((response: Response) => void)[] = []
    const fetchMock = vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          resolvers.push(resolve)
        }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const request = fetchWorkspaceOverview(new AbortController().signal)
    await vi.waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(4)
    })

    const bodies = [
      workspaceOverviewFixture.projects,
      workspaceOverviewFixture.focus,
      workspaceOverviewFixture.stats,
      workspaceOverviewFixture.session,
    ]
    resolvers.forEach((resolve, index) => {
      resolve(jsonResponse(bodies[index]))
    })

    await expect(request).resolves.toEqual(workspaceOverviewFixture)
  })

  it('rejects an invalid API contract', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ unexpected: true }))),
    )

    await expect(fetchWorkspaceOverview(new AbortController().signal)).rejects.toThrow()
  })
})
