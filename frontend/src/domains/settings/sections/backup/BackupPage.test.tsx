import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { BackupPage } from './BackupPage'

describe('BackupPage', () => {
  it('renders exports and asynchronously previews an import manifest', async () => {
    const user = userEvent.setup()
    const queryClient = new QueryClient()
    render(
      <QueryClientProvider client={queryClient}>
        <AppEventBusProvider>
          <BackupPage />
        </AppEventBusProvider>
      </QueryClientProvider>,
    )

    expect(screen.getByRole('link', { name: 'Download backup' })).toHaveAttribute(
      'href',
      '/backup/export',
    )
    expect(screen.getByRole('link', { name: 'Download job code' })).toHaveAttribute(
      'href',
      '/backup/jobs-code',
    )

    const file = new File(
      [JSON.stringify({ projects: [{}], jobs: [{}, {}], jobChains: [{}] })],
      'workspace.json',
      { type: 'application/json' },
    )
    await user.upload(screen.getByLabelText('Backup manifest'), file)

    expect(
      await screen.findByText(
        (_, element) =>
          element?.tagName === 'P' &&
          element.textContent === 'Loaded workspace.json — 1 project(s), 2 job(s), 1 chain(s).',
      ),
    ).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Import into this workspace' }))
    expect(screen.getByRole('button', { name: 'Confirm import' })).toBeVisible()
  })
})
