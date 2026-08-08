import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchProjectGraph } from './project-graph-api'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const graph = {
  projectId,
  nodeCount: 1,
  linkCount: 0,
  nodes: [
    {
      id: 'hub',
      label: 'Atlas',
      degree: 0,
      isGod: true,
      content: null,
      kind: null,
      labeled: true,
      artifact: null,
    },
  ],
  links: [],
}

describe('Project graph API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the validated graph read model', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify(graph), { status: 200 }))
    await expect(fetchProjectGraph(projectId, new AbortController().signal)).resolves.toEqual(graph)
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/v1/projects/${projectId}/data-graph`,
      expect.objectContaining({ method: 'GET' }),
    )
  })
})
