import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { DashboardCharts } from './DashboardCharts'

describe('DashboardCharts', () => {
  it('renders stored charts with an Analytics edit link', () => {
    render(<DashboardCharts charts={dashboardFixture.charts} project={dashboardFixture.project} />)

    expect(screen.getByRole('heading', { name: 'Charts' })).toBeVisible()
    expect(screen.getByText('Runs by day')).toBeVisible()
    expect(screen.getByRole('link', { name: /edit in analytics/i })).toHaveAttribute(
      'href',
      `/project/${dashboardFixture.project?.id ?? ''}/analytics`,
    )
  })
})
