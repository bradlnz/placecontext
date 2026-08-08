import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { artifactFiltersQueryOptions } from '../../api/settings-queries'
import { ArtifactFiltersPage } from './ArtifactFiltersPage'

describe('ArtifactFiltersPage', () => {
  it('renders, adds, and validates artifact filter rules asynchronously', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(artifactFiltersQueryOptions.queryKey, {
      categories: [{ id: 'reports', label: 'Reports', prefixes: ['report_'] }],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <ArtifactFiltersPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )
    expect(screen.getByDisplayValue('Reports')).toBeVisible()
    await user.click(screen.getByRole('button', { name: '＋ Add filter' }))
    expect(screen.getAllByLabelText('Button label')).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Save artifact filters' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Every filter needs')
  })
})
