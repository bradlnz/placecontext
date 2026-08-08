import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { inspectorToolCallsQueryKey } from '../../api/inspector-query-options'
import { InspectorPage } from './InspectorPage'

const calls = [
  {
    id: 'call-1',
    tool: 'search_context',
    direction: 'inbound',
    project: 'Atlas',
    summary: 'Search roads',
    status: 'Ok',
    durationMs: 42,
    requestJson: '{"query":"roads"}',
    responseJson: '{"count":3}',
    at: '2026-08-08T00:00:00+00:00',
  },
  {
    id: 'call-2',
    tool: 'run_job',
    direction: 'outbound',
    project: 'Atlas',
    summary: 'Run ingestion',
    status: 'Warn',
    durationMs: 80,
    requestJson: '{"job":"ingest"}',
    responseJson: '{"status":"partial"}',
    at: '2026-08-08T00:01:00+00:00',
  },
]

describe('InspectorPage', () => {
  it('shows the latest call and selects another feed entry', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(inspectorToolCallsQueryKey, calls)
    render(
      <QueryClientProvider client={queryClient}>
        <InspectorPage />
      </QueryClientProvider>,
    )

    expect(screen.getByText('{"query":"roads"}')).toBeVisible()
    await user.click(screen.getByRole('button', { name: /run_job/i }))
    expect(screen.getByText('{"job":"ingest"}')).toBeVisible()
    expect(screen.getByText('{"status":"partial"}')).toBeVisible()
  })

  it('keeps the canonical empty state', () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(inspectorToolCallsQueryKey, [])
    render(
      <QueryClientProvider client={queryClient}>
        <InspectorPage />
      </QueryClientProvider>,
    )

    expect(screen.getByText(/No tool calls yet/)).toBeVisible()
    expect(screen.getByText(/Select a tool call/)).toBeVisible()
  })
})
