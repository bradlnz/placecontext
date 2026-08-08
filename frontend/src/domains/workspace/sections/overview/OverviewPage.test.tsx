import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { AppEventBusProvider } from '../../../../app/app-event-bus'
import { AsyncEventBus } from '../../../../shared/events/async-event-bus'
import { OverviewPage } from './OverviewPage'

vi.mock('./overview-query', async () => {
  const { workspaceOverviewFixture: fixture } = await import('../../../../test/fixtures/workspace')

  return {
    useWorkspaceOverview: () => ({
      data: fixture,
      isRefreshing: false,
    }),
  }
})

describe('OverviewPage', () => {
  it('composes all overview sections from query data', () => {
    render(
      <AppEventBusProvider>
        <OverviewPage />
      </AppEventBusProvider>,
    )

    expect(screen.getByLabelText('Workspace statistics')).toBeVisible()
    expect(screen.getByText('Current focus')).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Projects' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Open Atlas' })).toBeEnabled()
  })

  it('publishes an async refresh request from the refresh interaction', async () => {
    const user = userEvent.setup()
    const publish = vi.spyOn(AsyncEventBus.prototype, 'publish').mockResolvedValue()
    render(
      <AppEventBusProvider>
        <OverviewPage />
      </AppEventBusProvider>,
    )

    await user.click(screen.getByRole('button', { name: /refresh/i }))

    expect(publish).toHaveBeenCalledWith('workspace.overview-refresh-requested', {
      source: 'overview-header',
    })
  })
})
