import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { AppEventBusProvider, useAppEventBus } from '../../../../../app/app-event-bus'
import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import type { RunDashboardChainCommand } from '../../../model/dashboard'
import { ChainRunDialog } from './ChainRunDialog'

function ChainRequestProbe({ onRequest }: { onRequest: (command: RunDashboardChainCommand) => void }) {
  const eventBus = useAppEventBus()

  useEffect(() => eventBus.subscribe('dashboard.chain-run-requested', async (command) => {
    await Promise.resolve()
    onRequest(command)
  }), [eventBus, onRequest])

  return null
}

describe('ChainRunDialog', () => {
  it('renders stored defaults and validates required parameters', async () => {
    const user = userEvent.setup()
    const chain = dashboardFixture.chains[0]
    if (chain === undefined) throw new Error('Dashboard chain fixture is missing.')

    render(
      <AppEventBusProvider>
        <ChainRunDialog chain={chain} onClose={vi.fn()} running={false} />
      </AppEventBusProvider>,
    )

    const branch = screen.getByRole('textbox', { name: /branch/i })
    expect(branch).toHaveValue('main')
    await user.clear(branch)
    await user.click(screen.getByRole('button', { name: /run chain/i }))

    expect(screen.getByRole('alert')).toHaveTextContent('Required: step 1: Branch')
  })

  it('publishes a serialized async chain-run command', async () => {
    const user = userEvent.setup()
    const onRequest = vi.fn()
    const onClose = vi.fn()
    const chain = dashboardFixture.chains[0]
    if (chain === undefined) throw new Error('Dashboard chain fixture is missing.')

    render(
      <AppEventBusProvider>
        <ChainRequestProbe onRequest={onRequest} />
        <ChainRunDialog chain={chain} onClose={onClose} running={false} />
      </AppEventBusProvider>,
    )

    await user.click(screen.getByRole('button', { name: /run chain/i }))

    expect(onRequest).toHaveBeenCalledWith(expect.objectContaining({
      projectId: chain.projectId,
      chainId: chain.id,
      stepPayloadOverrides: { 0: '{"branch":"main"}' },
    }))
    expect(onClose).toHaveBeenCalledOnce()
  })
})
