import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { apiTokensQueryOptions } from '../../api/api-tokens-query'
import { ApiTokensPage } from './ApiTokensPage'

describe('ApiTokensPage', () => {
  it('replicates endpoint documentation, creation, and active token controls', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(apiTokensQueryOptions.queryKey, [
      {
        id: '7b4eed95-c20a-4522-86f4-2f9ae5891302',
        name: 'CI',
        tokenPrefix: 'pc_123456',
        createdAt: '2026-08-08T00:00:00+00:00',
        lastUsedAt: null,
        expiresAt: null,
      },
    ])
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <ApiTokensPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByText('GET /api/v1/entities')).toBeVisible()
    expect(screen.getByText('pc_123456…')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Give the token a name.')
    expect(screen.getByRole('button', { name: 'Revoke' })).toBeEnabled()
  })
})
