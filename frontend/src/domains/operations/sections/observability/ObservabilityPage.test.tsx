import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { observabilityQueryKeys } from '../../api/observability-query'
import type { ObservabilityPageModel } from '../../model/observability'
import { ObservabilityPage } from './ObservabilityPage'

const runId = 'b32624cf-70e9-42aa-877c-7e0ff0f4af8d'
const jobId = '41aeddf7-723c-40fe-9dd3-a61a6e6388b4'

const page: ObservabilityPageModel = {
  canReplay: true,
  chains: [
    {
      projectId: '05629e1f-b6ed-47e9-9267-8ac79288e158',
      projectName: 'Brisbane planning',
      run: {
        chainId: 'ef283ff6-f714-4731-9a74-f3b1e3b82d44',
        chainName: 'Planning report',
        finalOutput: '{"result":"ready"}',
        finishedAt: '2026-08-09T03:02:00+00:00',
        id: 'b745257c-29d9-4ce7-a6fe-6386bc2ba90c',
        startedAt: '2026-08-09T03:00:00+00:00',
        status: 'Succeeded',
        steps: [
          {
            branchIndex: 0,
            error: null,
            finishedAt: '2026-08-09T03:02:00+00:00',
            index: 0,
            jobId,
            jobName: 'Assess site',
            runId,
            stageIndex: 0,
            startedAt: '2026-08-09T03:00:00+00:00',
            status: 'Succeeded',
          },
        ],
      },
    },
  ],
  liveTraces: [],
  runs: [
    {
      jobId,
      jobName: 'Assess site',
      projectName: 'Brisbane planning',
      run: {
        attemptNumber: 1,
        finishedAt: '2026-08-09T03:02:00+00:00',
        id: runId,
        jobId,
        originalRunId: null,
        projectId: '05629e1f-b6ed-47e9-9267-8ac79288e158',
        reduceResult: null,
        shardResults: [
          {
            artifacts: [],
            artifact: '{"lots":12}',
            exitCode: 0,
            index: 0,
            log: null,
            outcome: 'Succeeded',
          },
        ],
        snapshot: {
          allowNetworkEgress: false,
          concurrencyLimit: 2,
          mapSourceKind: 'Python',
          mapSourceLabel: 'main.py',
          reduceSourceKind: null,
          reduceSourceLabel: null,
          shardCount: 1,
        },
        startedAt: '2026-08-09T03:00:00+00:00',
        status: 'Succeeded',
      },
    },
  ],
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  queryClient.setQueryData(observabilityQueryKeys.page, page)
  render(
    <MemoryRouter initialEntries={['/observability']}>
      <QueryClientProvider client={queryClient}>
        <ObservabilityPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ObservabilityPage', () => {
  it('switches lenses and opens a chain pipeline', async () => {
    const user = userEvent.setup()
    renderPage()

    expect(screen.getByText('Assess site')).toBeVisible()
    await user.click(screen.getByRole('button', { name: /Chains 1/ }))
    await user.click(screen.getByRole('button', { name: /Planning report/ }))

    expect(screen.getByRole('dialog', { name: /Planning report chain run details/ })).toBeVisible()
    expect(screen.getByRole('button', { name: /Assess site/ })).toBeVisible()
    expect(screen.getByText(/"result": "ready"/)).toBeVisible()
  })
})
