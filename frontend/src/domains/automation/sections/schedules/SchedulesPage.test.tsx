import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { schedulesQueryOptions } from '../../api/schedules-query'
import { SchedulesPage } from './SchedulesPage'
const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'
vi.mock('react-router-dom', () => ({ useParams: () => ({ projectId }) }))
describe('SchedulesPage', () => {
  it('renders timezone and trigger state', () => {
    const client = new QueryClient()
    client.setQueryData(schedulesQueryOptions(projectId).queryKey, {
      timeZoneId: 'Australia/Brisbane',
      jobs: [],
      chains: [],
      tables: [],
      eventTypes: [],
      triggers: [],
    })
    render(
      <QueryClientProvider client={client}>
        <SchedulesPage />
      </QueryClientProvider>,
    )
    expect(screen.getByText('Australia/Brisbane')).toBeVisible()
    expect(screen.getByText(/No triggers yet/)).toBeVisible()
  })
})
