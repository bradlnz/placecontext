import { useEffect, useId } from 'react'

import { loadHostChartRenderer } from '../../../../../shared/charts/load-host-chart-renderer'
import type { DashboardChart as DashboardChartModel } from '../../../model/dashboard'

interface DashboardChartProps {
  chart: DashboardChartModel
}

function formatChartDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function DashboardChart({ chart }: DashboardChartProps) {
  const reactId = useId()
  const canvasId = `pcdash-${reactId.replaceAll(':', '')}`

  useEffect(() => {
    let active = true
    let renderer: Awaited<ReturnType<typeof loadHostChartRenderer>> | undefined
    void loadHostChartRenderer().then((loadedRenderer) => {
      renderer = loadedRenderer
      if (active) loadedRenderer.render(canvasId, chart.spec)
    })

    return () => {
      active = false
      renderer?.destroy(canvasId)
    }
  }, [canvasId, chart.spec])

  return (
    <article className="dccard dashboard-chart-card">
      <div className="chart-card-head">
        <span className="chart-title">{chart.name}</span>
        <span className="spacer" />
        <time className="chart-time" dateTime={chart.generatedAt}>
          {formatChartDate(chart.generatedAt)}
        </time>
      </div>
      <div className="chart-canvas-wrap">
        <canvas aria-label={`${chart.name} chart`} id={canvasId} role="img" />
      </div>
    </article>
  )
}
