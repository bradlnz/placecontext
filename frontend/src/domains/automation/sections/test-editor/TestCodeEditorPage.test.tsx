import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { jobTestCodeQueryOptions } from '../../api/job-tests-query'
import { TestCodeEditorPage } from './TestCodeEditorPage'

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
  lastStatus: 'NotRun',
  lastMessage: null,
  lastActualOutput: null,
  lastDurationMs: null,
  runtimeId: 'python',
  runtimeLabel: 'pytest',
  entrypoint: 'test_job.py',
  codeFiles: [
    { path: 'test_job.py', content: 'def test_ok(): pass' },
    { path: 'requirements.txt', content: 'pytest==8.4.1' },
  ],
  methodResults: [{ name: 'test_ok', status: 'NotRun', durationMs: null, message: null }],
}
const context = {
  test: block,
  runtimes: [
    {
      id: 'python',
      label: 'Python',
      frameworkLabel: 'pytest',
      entrypoint: 'test_job.py',
      starterFiles: block.codeFiles,
    },
    {
      id: 'node',
      label: 'Node.js',
      frameworkLabel: 'Node test',
      entrypoint: 'job.test.js',
      starterFiles: [{ path: 'job.test.js', content: 'test("ok", () => {})' }],
    },
  ],
}

describe('TestCodeEditorPage', () => {
  beforeEach(() => {
    window.pcmonaco = {
      init: vi.fn().mockResolvedValue(true),
      openFile: vi.fn(),
      closeFile: vi.fn(),
      getValue: vi.fn().mockReturnValue('def test_ok(): pass'),
      destroy: vi.fn(),
    }
  })
  it('shows discovered methods, files, isolation, and runtime starters', async () => {
    const user = userEvent.setup()
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    client.setQueryData(jobTestCodeQueryOptions(projectId, testId).queryKey, context)
    const router = createMemoryRouter(
      [
        {
          path: '/project/:projectId/tests/:testId',
          element: <TestCodeEditorPage />,
        },
      ],
      { initialEntries: [`/project/${projectId}/tests/${testId}`] },
    )
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    expect(screen.getByText('test_ok')).toBeVisible()
    expect(screen.getByText('requirements.txt')).toBeVisible()
    expect(screen.getByText('Isolated')).toBeVisible()
    await user.selectOptions(screen.getByRole('combobox', { name: 'Runtime' }), 'node')
    await user.click(screen.getByRole('button', { name: 'Starter' }))
    expect(screen.getAllByText('job.test.js')).toHaveLength(2)
    expect(screen.getByText(/Node.js starter loaded/)).toBeVisible()
  })
})
