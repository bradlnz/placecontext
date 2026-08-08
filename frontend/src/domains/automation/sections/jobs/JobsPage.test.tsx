import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { jobsQueryOptions } from '../../api/jobs-query'
import type { Job } from '../../model/jobs'
import { JobsPage } from './JobsPage'

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
  inputPayloads: ['{}', '{"page":2}'],
  mapEnv: {},
  reduceSourceKind: null,
  reduceImage: null,
  reduceRuntimeId: null,
  reduceSource: null,
  reduceEntrypoint: null,
  reduceFiles: [],
  reduceEnv: null,
  concurrencyLimit: 2,
  successExitCodes: [0],
  partialExitCodes: [],
  allowNetworkEgress: true,
  allowApiInvocation: false,
  parameters: [],
  postJobActions: [],
  returnType: 'Json',
  returnFileName: null,
  retryCount: 1,
  retryDelaySeconds: 3,
  mcpConnectionIds: [],
  createdAt: '2026-08-08T00:00:00Z',
  updatedAt: '2026-08-08T00:00:00Z',
}

describe('JobsPage', () => {
  it('summarises workloads and opens advanced job details', async () => {
    const user = userEvent.setup()
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    client.setQueryData(jobsQueryOptions(projectId).queryKey, {
      jobs: [job],
      triggers: [
        {
          id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
          jobId,
          name: 'Nightly',
          kind: 'Schedule',
          enabled: true,
          cronExpression: '0 0 * * *',
          eventName: null,
        },
      ],
    })
    const router = createMemoryRouter(
      [{ path: '/project/:projectId/jobs', element: <JobsPage /> }],
      { initialEntries: [`/project/${projectId}/jobs`] },
    )
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    expect(screen.getByText('1 automated')).toBeVisible()
    expect(screen.getByText(/2 shards/)).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Edit Import customers' }))
    expect(screen.getByDisplayValue('Pull customer records')).toBeVisible()
    expect(screen.getByDisplayValue('0')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Triggers' }))
    expect(screen.getByText('Nightly')).toBeVisible()
  })
})
