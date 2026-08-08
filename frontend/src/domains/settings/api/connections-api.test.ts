import { afterEach, describe, expect, it, vi } from 'vitest'

import { fetchConnections, resetExternalDatabase, saveExternalIndex } from './connections-api'

const project = {
  id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
  name: 'Atlas',
  hasExternalDatabase: false,
  hasExternalIndex: true,
}

describe('connections API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads the composed context from the canonical api/v1 route', async () => {
    const body = { projects: [project], sslModes: ['Prefer', 'Require'] }
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))
    await expect(fetchConnections(new AbortController().signal)).resolves.toEqual(body)
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/settings/connections/context', expect.objectContaining({ method: 'GET' }))
  })

  it('writes and resets project connection settings', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(() => Promise.resolve(new Response(JSON.stringify(project), { status: 200 })))
    const signal = new AbortController().signal
    await saveExternalIndex(project.id, { endpoint: 'https://search.example.com', username: '', password: '', index: '*' }, signal)
    await resetExternalDatabase(project.id, signal)
    expect(fetchMock).toHaveBeenNthCalledWith(1, `/api/v1/settings/connections/projects/${project.id}/index`, expect.objectContaining({ method: 'PUT' }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, `/api/v1/settings/connections/projects/${project.id}/database`, expect.objectContaining({ method: 'DELETE' }))
  })
})
