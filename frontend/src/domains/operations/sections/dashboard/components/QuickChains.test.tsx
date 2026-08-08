import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AppEventBusProvider } from '../../../../../app/app-event-bus'
import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { QuickChains } from './QuickChains'

describe('QuickChains', () => {
  it('opens the parameter dialog for a declared-input chain', async () => {
    const user = userEvent.setup()
    render(
      <AppEventBusProvider>
        <QuickChains
          chains={dashboardFixture.chains}
          hasError={false}
          message={null}
          project={dashboardFixture.project}
          runningChainId={null}
        />
      </AppEventBusProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Run Daily context refresh' }))

    expect(screen.getByRole('dialog', { name: 'Run Daily context refresh' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: /branch/i })).toHaveValue('main')
  })

  it('renders the Host empty state without a project', () => {
    render(
      <AppEventBusProvider>
        <QuickChains chains={[]} hasError={false} message={null} project={null} runningChainId={null} />
      </AppEventBusProvider>,
    )

    expect(screen.getByText('Select a project to run its job chains.')).toBeVisible()
  })
})
