import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { jobTestsQueryOptions } from '../../api/job-tests-query'
import { TestsPage } from './TestsPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
const jobId = '79d2d944-56ef-4597-a64d-10b56c18e33d'
const testId = '158fdb23-5c46-4777-b0bb-d78ff91b8754'
const block = {
  id: testId,
  projectId,
  jobId,
  jobName: 'Import',
  name: 'customer contract',
  inputPayload: '{}',
  assertionType: 'Succeeds' as const,
  expectedValue: null,
  enabled: true,
  lastStatus: 'Passed',
  lastMessage: '2 methods passed.',
  lastActualOutput: null,
  lastDurationMs: 45,
  runtimeId: 'python',
  runtimeLabel: 'pytest',
  entrypoint: 'test_job.py',
  codeFiles: [{ path: 'test_job.py', content: 'def test_ok(): pass' }],
  methodResults: [
    { name: 'test_ok', status: 'Passed', durationMs: 10, message: null },
    { name: 'test_output', status: 'Passed', durationMs: 12, message: null },
  ],
}

describe('TestsPage', () => {
  it('renders method summaries and opens the canonical block editor', async () => {
    const user = userEvent.setup()
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    client.setQueryData(jobTestsQueryOptions(projectId).queryKey, {
      jobs: [{ id: jobId, name: 'Import' }],
      tests: [block],
    })
    const router = createMemoryRouter(
      [{ path: '/project/:projectId/tests', element: <TestsPage /> }],
      { initialEntries: [`/project/${projectId}/tests`] },
    )
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    const summary = screen.getByLabelText('Test summary')
    expect(within(summary).getByText('methods')).toBeVisible()
    expect(screen.getByText('2 / 2 passing')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Edit customer contract' }))
    expect(screen.getByRole('dialog', { name: 'Edit block' })).toBeVisible()
    expect(screen.getByDisplayValue('customer contract')).toBeVisible()
  })
})
