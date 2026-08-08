import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { communicationsQueryOptions } from '../../api/communications-query'
import { providerFixture } from './CommunicationProviderList.test'
import { CommunicationsPage } from './CommunicationsPage'

describe('CommunicationsPage', () => {
  it('composes the provider list and asynchronously opens the editor', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(communicationsQueryOptions.queryKey, {
      providers: [providerFixture],
      projects: [{ id: 'a102ed75-e94a-48fe-9826-2532d524857f', name: 'Atlas' }],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <CommunicationsPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByText('Transactional email')).toBeVisible()
    await user.click(screen.getByRole('button', { name: '+ Add provider' }))
    expect(screen.getByRole('heading', { name: 'New provider' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Add provider' })).toBeDisabled()
  })
})
