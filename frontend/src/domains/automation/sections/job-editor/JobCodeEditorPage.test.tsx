import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { jobCodeQueryOptions } from '../../api/jobs-query'
import type { Job } from '../../model/jobs'
import { JobCodeEditorPage } from './JobCodeEditorPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const jobId = '79d2d944-56ef-4597-a64d-10b56c18e33d'
const job: Job = {
  id: jobId,
  projectId,
  name: 'Import customers',
  description: 'Pull customer records',
  mapSourceKind: 'code',
  mapImage: null,
  mapRuntimeId: 'node',
  mapSource: null,
  mapEntrypoint: 'index.js',
  mapFiles: [
    { path: 'index.js', content: 'console.log({ ok: true })' },
    { path: 'package.json', content: '{}' },
  ],
  inputPayloads: ['{}'],
  mapEnv: {},
  reduceSourceKind: null,
  reduceImage: null,
  reduceRuntimeId: null,
  reduceSource: null,
  reduceEntrypoint: null,
  reduceFiles: [],
  reduceEnv: null,
  concurrencyLimit: 1,
  successExitCodes: [0],
  partialExitCodes: [],
  allowNetworkEgress: false,
  allowApiInvocation: false,
  parameters: [],
  postJobActions: [],
  returnType: 'Json',
  returnFileName: null,
  retryCount: 0,
  retryDelaySeconds: 0,
  mcpConnectionIds: [],
  createdAt: '2026-08-08T00:00:00Z',
  updatedAt: '2026-08-08T00:00:00Z',
}

describe('JobCodeEditorPage', () => {
  beforeEach(() => {
    window.pcmonaco = {
      init: vi.fn().mockResolvedValue(true),
      openFile: vi.fn(),
      closeFile: vi.fn(),
      getValue: vi.fn().mockReturnValue('console.log({ ok: true })'),
      destroy: vi.fn(),
    }
  })
  it('renders multi-file controls and preserves entrypoint operations', async () => {
    const user = userEvent.setup()
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    client.setQueryData(jobCodeQueryOptions(projectId, jobId).queryKey, {
      job,
    })
    const router = createMemoryRouter(
      [
        {
          path: '/project/:projectId/jobs/:jobId',
          element: <JobCodeEditorPage />,
        },
      ],
      { initialEntries: [`/project/${projectId}/jobs/${jobId}`] },
    )
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    expect(screen.getAllByText('index.js')).toHaveLength(2)
    expect(screen.getByText('package.json')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Rename package.json' }))
    const input = screen.getByDisplayValue('package.json')
    await user.clear(input)
    await user.type(input, 'manifest.json{Enter}')
    expect(screen.getByText('manifest.json')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Set manifest.json as entrypoint' })).toBeEnabled()
  })
})
