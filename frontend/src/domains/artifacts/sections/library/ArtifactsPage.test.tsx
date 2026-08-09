import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { workspaceOverviewFixture } from '../../../../test/fixtures/workspace'
import { workspaceQueryKeys } from '../../../workspace/api/workspace-query-options'
import { artifactsQueryKeys } from '../../api/artifacts-query'
import type { ArtifactsPageModel } from '../../model/artifacts'
import { ArtifactsPage } from './ArtifactsPage'

const api = vi.hoisted(() => ({
  artifactDownloadUrl: vi.fn((artifact: { id: string }) => `/download/${artifact.id}`),
  createArtifactShare: vi.fn(),
  deleteArtifacts: vi.fn(),
  fetchArtifactShareStatus: vi.fn(),
  fetchArtifactText: vi.fn(),
  revokeArtifactShare: vi.fn(),
}))

vi.mock('../../api/artifacts-api', () => api)

const project = firstWorkspaceProject()
const page: ArtifactsPageModel = {
  files: [
    {
      id: 'e4aa2f95-33bd-43aa-ac9b-84f016987613',
      runId: '63c07b5a-43ba-44cb-92d1-8ca98e4fe12a',
      jobId: '968d5817-d0e4-4dfb-a338-3b04b12ecad4',
      projectId: project.id,
      kind: 'Report',
      title: 'report.json',
      contentType: 'application/json',
      sizeBytes: 128,
      createdAt: '2026-08-09T02:00:00+00:00',
    },
    {
      id: '8ea66442-3e00-44f5-942e-5503957d2f5a',
      runId: 'fbc22c5a-20bd-421d-938a-a7dde66c07df',
      jobId: '968d5817-d0e4-4dfb-a338-3b04b12ecad4',
      projectId: project.id,
      kind: 'Report',
      title: 'report.json',
      contentType: 'application/json',
      sizeBytes: 96,
      createdAt: '2026-08-08T02:00:00+00:00',
    },
  ],
  projects: [{ id: project.id, name: project.name }],
  config: { categories: [{ id: 'reports', label: 'Reports', prefixes: ['report'] }] },
  canDelete: true,
  canShare: true,
  canManageSettings: true,
  loadMayBeIncomplete: false,
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  queryClient.setQueryData(workspaceQueryKeys.projects, workspaceOverviewFixture.projects)
  queryClient.setQueryData(artifactsQueryKeys.page(project.id, ''), page)
  render(
    <MemoryRouter initialEntries={['/artifacts']}>
      <QueryClientProvider client={queryClient}>
        <ArtifactsPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ArtifactsPage', () => {
  beforeEach(() => {
    api.deleteArtifacts.mockReset()
    api.fetchArtifactText.mockReset()
    api.fetchArtifactText.mockResolvedValue('{"status":"ready","rows":42}')
  })

  it('groups artifact versions and opens a structured preview', async () => {
    const user = userEvent.setup()
    renderPage()

    expect(screen.getByRole('button', { name: /Show 2 versions/ })).toBeVisible()
    await user.click(screen.getByText('report.json'))

    expect(await screen.findByText(/"status": "ready"/)).toBeVisible()
    expect(screen.getByRole('link', { name: 'Open ↗' })).toHaveAttribute(
      'href',
      `/download/${page.files[0]?.id ?? ''}`,
    )
  })

  it('bulk deletes explicitly selected artifact versions', async () => {
    const user = userEvent.setup()
    api.deleteArtifacts.mockResolvedValue({ deleted: 2 })
    renderPage()

    await user.click(screen.getByRole('checkbox', { name: 'Select report.json' }))
    await user.click(screen.getByRole('button', { name: 'Delete selected' }))

    expect(api.deleteArtifacts).toHaveBeenCalledWith(
      page.files.map((artifact) => artifact.id),
      expect.any(AbortSignal),
    )
  })
})

function firstWorkspaceProject() {
  const first = workspaceOverviewFixture.projects[0]
  if (first === undefined) throw new Error('Workspace fixture needs a project.')
  return first
}
