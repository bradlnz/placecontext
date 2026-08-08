import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { dashboardFixture } from '../../../../../test/fixtures/dashboard'
import { DashboardChart } from './DashboardChart'

describe('DashboardChart', () => {
  afterEach(() => {
    delete window.pcchart
  })

  it('renders and disposes a chart through the Host chart renderer', async () => {
    const renderChart = vi.fn()
    const destroyChart = vi.fn()
    window.pcchart = { render: renderChart, destroy: destroyChart }
    const chart = dashboardFixture.charts[0]
    if (chart === undefined) throw new Error('Dashboard chart fixture is missing.')

    const view = render(<DashboardChart chart={chart} />)

    expect(screen.getByRole('img', { name: 'Runs by day chart' })).toBeVisible()
    await waitFor(() => {
      expect(renderChart).toHaveBeenCalledWith(expect.any(String), chart.spec)
    })

    view.unmount()
    expect(destroyChart).toHaveBeenCalledWith(expect.any(String))
  })
})
