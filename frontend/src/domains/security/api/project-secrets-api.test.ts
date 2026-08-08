import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  createProjectSecret,
  deleteProjectSecret,
  fetchProjectSecrets,
} from './project-secrets-api'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const secret = {
  name: 'API_KEY',
  createdAt: '2026-08-08T00:00:00+00:00',
  createdAtDisplay: '2026-08-08 10:00',
}

describe('Project secrets API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('lists, creates, and deletes secrets through api/v1', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => {
      if (init?.method === 'DELETE') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(
        new Response(JSON.stringify(init?.method === 'POST' ? secret : [secret]), { status: 200 }),
      )
    })
    const signal = new AbortController().signal

    await expect(fetchProjectSecrets(projectId, signal)).resolves.toEqual([secret])
    await expect(
      createProjectSecret(projectId, { name: 'API_KEY', value: 'secret' }, signal),
    ).resolves.toEqual(secret)
    await expect(deleteProjectSecret(projectId, 'API KEY', signal)).resolves.toBeUndefined()
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/projects/${projectId}/secrets`,
      expect.objectContaining({ method: 'GET' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/projects/${projectId}/secrets`,
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/projects/${projectId}/secrets/API%20KEY`,
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
