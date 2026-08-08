import { describe, expect, it, vi } from 'vitest'

import { importBackupManifest, readBackupManifest } from './backup-api'

describe('backup API', () => {
  it('validates a selected manifest before enabling import', async () => {
    const file = new File([
      JSON.stringify({ projects: [{}], jobs: [{}, {}], jobChains: [] }),
    ], 'backup.json', { type: 'application/json' })

    await expect(readBackupManifest(file)).resolves.toMatchObject({
      fileName: 'backup.json',
      projectCount: 1,
      jobCount: 2,
      chainCount: 0,
    })
  })

  it('imports through the versioned PlaceContext API contract', async () => {
    const result = {
      projectsCreated: 1,
      projectsUpdated: 0,
      jobsCreated: 2,
      jobsUpdated: 1,
      jobsSkipped: 0,
      warnings: [],
    }
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify(result), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    vi.stubGlobal('fetch', fetchMock)

    await expect(importBackupManifest({ projects: [], jobs: [], jobChains: [] }, new AbortController().signal)).resolves.toEqual(result)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/settings/backup/imports',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})
