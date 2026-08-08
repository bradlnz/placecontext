import { describe, expect, it, vi } from 'vitest'

import { dashboardFixture } from '../../../test/fixtures/dashboard'
import { fetchDashboard, runDashboardChain } from './dashboard-api'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('dashboard API', () => {
  it('loads and validates the Dashboard API contract', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(dashboardFixture))),
    )

    await expect(fetchDashboard(new AbortController().signal)).resolves.toEqual(dashboardFixture)
  })

  it('posts a typed chain command to the PlaceContext API', async () => {
    const result = {
      chainRunId: '4dc2d460-4957-44a4-aed7-b4ba35950507',
      message: 'Chain started.',
    }
    const fetchMock = vi.fn(() => Promise.resolve(jsonResponse(result, 202)))
    vi.stubGlobal('fetch', fetchMock)

    const command = {
      projectId: dashboardFixture.project?.id ?? '',
      chainId: dashboardFixture.chains[0]?.id ?? '',
      inputPayload: null,
      stepPayloadOverrides: null,
    }
    await expect(runDashboardChain(command, new AbortController().signal)).resolves.toEqual(result)
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/v1/dashboard/projects/${command.projectId}/chains/${command.chainId}/runs`,
      expect.objectContaining({ method: 'POST', credentials: 'same-origin' }),
    )
  })

  it('rejects an invalid Dashboard payload before it reaches components', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ project: 'invalid' }))),
    )

    await expect(fetchDashboard(new AbortController().signal)).rejects.toThrow()
  })
})
