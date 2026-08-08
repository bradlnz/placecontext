import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { menuQueryOptions } from '../../api/settings-queries'
import { MenuPage } from './MenuPage'

describe('MenuPage', () => {
  it('renders and asynchronously reorders every sidebar item', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(menuQueryOptions.queryKey, {
      workspace: [
        {
          id: 'dashboard',
          defaultLabel: 'Dashboard',
          label: '',
          order: 0,
          visible: true,
          section: '',
        },
        {
          id: 'overview',
          defaultLabel: 'Projects overview',
          label: '',
          order: 10,
          visible: true,
          section: 'Workspace',
        },
      ],
    })
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <MenuPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )

    const list = screen.getByRole('region', { name: 'Sidebar items' })
    expect(within(list).getAllByRole('article')).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Move Projects overview up' }))
    const rows = within(list).getAllByRole('article')
    const firstRow = rows[0]
    expect(firstRow).toBeDefined()
    if (firstRow === undefined) throw new Error('Expected the reordered first menu row.')
    expect(within(firstRow).getByText('Projects overview')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Save menu' })).toBeEnabled()
  })
})
