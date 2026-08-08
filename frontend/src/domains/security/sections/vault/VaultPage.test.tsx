import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { projectSecretsQueryOptions } from '../../api/project-secrets-query'
import { VaultPage } from './VaultPage'

const projectId = 'a102ed75-e94a-48fe-9826-2532d524857f'

vi.mock('react-router-dom', () => ({
  useParams: () => ({ projectId }),
}))

describe('VaultPage', () => {
  it('lists only secret metadata and validates the create form', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    queryClient.setQueryData(projectSecretsQueryOptions(projectId).queryKey, [
      {
        name: 'API_KEY',
        createdAt: '2026-08-08T00:00:00+00:00',
        createdAtDisplay: '2026-08-08 10:00',
      },
    ])
    render(
      <QueryClientProvider client={queryClient}>
        <VaultPage />
      </QueryClientProvider>,
    )

    expect(screen.getByText('API_KEY')).toBeVisible()
    expect(screen.getByText('••••••••')).toBeVisible()
    expect(screen.queryByText('secret-value')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Add' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Name is required.')
  })
})
