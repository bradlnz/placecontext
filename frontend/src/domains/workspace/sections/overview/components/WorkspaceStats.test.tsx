import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { workspaceOverviewFixture } from '../../../../../test/fixtures/workspace'
import { WorkspaceStats } from './WorkspaceStats'

describe('WorkspaceStats', () => {
  it('renders formatted workspace metrics and warning state', () => {
    render(<WorkspaceStats stats={{ ...workspaceOverviewFixture.stats, projectCount: 1234 }} />)

    expect(screen.getByText('1,234')).toBeVisible()
    expect(screen.getByText('5 agent · 3 human')).toBeVisible()
    expect(screen.getByText('need re-index').closest('article')?.querySelector('.stat-value')).toHaveStyle({ color: 'var(--warn)' })
  })

  it('uses the positive state when context is current', () => {
    render(<WorkspaceStats stats={{ ...workspaceOverviewFixture.stats, staleContextCount: 0 }} />)

    expect(screen.getByText('all current').closest('article')?.querySelector('.stat-value')).toHaveStyle({ color: 'var(--good)' })
  })
})
