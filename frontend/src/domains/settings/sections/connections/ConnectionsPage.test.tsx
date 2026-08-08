import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { connectionsQueryOptions } from '../../api/connections-query'
import { ConnectionsPage } from './ConnectionsPage'

describe('ConnectionsPage', () => {
  it('shows project connection status and validates the database form', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(connectionsQueryOptions.queryKey, {
      projects: [
        {
          id: '158fdb23-5c46-4777-b0bb-d78ff91b8754',
          name: 'Atlas',
          hasExternalDatabase: false,
          hasExternalIndex: true,
        },
      ],
      sslModes: ['Prefer', 'Require'],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <ConnectionsPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(
      screen.getByText(/Atlas — using the cluster database · external search index configured/),
    ).toBeVisible()
    expect(screen.getByRole('button', { name: 'Reset to workspace default' })).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Save external database' }))
    expect(screen.getAllByRole('alert')[0]).toHaveTextContent('Host is required.')
  })
})
