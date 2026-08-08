import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { RecentJobs } from './RecentJobs'

describe('RecentJobs', () => {
  it('filters recent jobs through async controls', async () => {
    const user = userEvent.setup()
    render(<RecentJobs runs={dashboardFixture.recentRuns} />)

    expect(screen.getByText('Build context')).toBeVisible()
    expect(screen.getByText('Publish artifacts')).toBeVisible()

    await user.click(screen.getByRole('button', { name: 'failed' }))

    expect(screen.queryByText('Build context')).not.toBeInTheDocument()
    expect(screen.getByText('Publish artifacts')).toBeVisible()
  })
})
