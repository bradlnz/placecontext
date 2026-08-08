import { afterEach, describe, expect, it, vi } from 'vitest'

import { createClusterJoinCommand, fetchCluster } from './cluster-api'

const cluster = {
  isRealCluster: false,
  designatedMasterName: 'local',
  nodes: [],
  lastSyncLabel: '10:00:00',
}

describe('Cluster API', () => {
  afterEach(() => vi.restoreAllMocks())

  it('loads fleet state and creates a one-time worker command', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation((_input, init) =>
        Promise.resolve(
          new Response(
            JSON.stringify(init?.method === 'POST' ? { command: 'curl join' } : cluster),
            { status: 200 },
          ),
        ),
      )
    const signal = new AbortController().signal
    await expect(fetchCluster(signal)).resolves.toEqual(cluster)
    await expect(createClusterJoinCommand(signal)).resolves.toBe('curl join')
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/cluster',
      expect.objectContaining({ method: 'GET' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/cluster/workers/join-command',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
