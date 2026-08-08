import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { dashboardFixture } from '../../../../test/fixtures/dashboard'
import { dashboardQueryKey } from '../../api/dashboard-query-options'
import { DashboardPage } from './DashboardPage'

describe('DashboardPage', () => {
  it('composes every Dashboard section from the PlaceContext API contract', async () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(dashboardQueryKey, dashboardFixture)

    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <DashboardPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeVisible()
    expect(screen.getByText('Daily context refresh')).toBeVisible()
    expect(screen.getByText('Customers')).toBeVisible()
    expect(screen.getByText('Runs by day')).toBeVisible()
    expect(screen.getByText('Build context')).toBeVisible()
  })
})
