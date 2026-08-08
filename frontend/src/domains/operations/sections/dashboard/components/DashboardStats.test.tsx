import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { DashboardStats } from './DashboardStats'

describe('DashboardStats', () => {
  it('renders all four Host job statistics', () => {
    render(<DashboardStats stats={dashboardFixture.stats} />)

    expect(screen.getByRole('region', { name: 'Job statistics' })).toBeVisible()
    expect(screen.getByText('RUNNING')).toBeVisible()
    expect(screen.getByText('FAILED · 24H')).toBeVisible()
    expect(screen.getByText('17')).toBeVisible()
  })
})
