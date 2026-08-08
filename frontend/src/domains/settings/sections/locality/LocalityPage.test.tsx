import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { localityQueryOptions } from '../../api/settings-queries'
import { LocalityPage } from './LocalityPage'

describe('LocalityPage', () => {
  it('renders server-supported timezones and updates the preview selection', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    queryClient.setQueryData(localityQueryOptions.queryKey, {
      timeZoneId: 'UTC', timeZones: ['UTC', 'Australia/Brisbane'],
    })
    render(<QueryClientProvider client={queryClient}><AppEventBusProvider><LocalityPage /></AppEventBusProvider></QueryClientProvider>)

    const picker = screen.getByLabelText(/Workspace timezone/)
    await user.selectOptions(picker, 'Australia/Brisbane')
    expect(picker).toHaveValue('Australia/Brisbane')
    expect(screen.getAllByText('Australia/Brisbane')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Save timezone' })).toBeEnabled()
  })
})
