import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchProjectPage, updateProjectRequirements } from './project-page-api'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const requirements = {
  markdown: '# Rules',
  updatedAt: '2026-08-08T00:00:00+00:00',
  updatedAtDisplay: '2026-08-08 10:00',
}
const context = {
  overview: {
    id: projectId,
    name: 'Atlas',
    path: '/code/atlas',
    status: 'Active',
    godNodes: [],
  },
  timeline: { changes: [] },
  decisions: [],
  requirements,
  message: null,
}

describe('Project page API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the composed context and saves requirements', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) =>
      Promise.resolve(
        new Response(JSON.stringify(init?.method === 'PUT' ? requirements : context), {
          status: 200,
        }),
      ),
    )
    const signal = new AbortController().signal
    await expect(fetchProjectPage(projectId, signal)).resolves.toEqual(context)
    await expect(updateProjectRequirements(projectId, '# Rules', signal)).resolves.toEqual(
      requirements,
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/projects/${projectId}/overview-context`,
      expect.objectContaining({ method: 'GET' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/projects/${projectId}/requirements`,
      expect.objectContaining({ method: 'PUT' }),
    )
  })
})
