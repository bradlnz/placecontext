import { useEffect, useId } from 'react'
import { loadHostChartRenderer } from '../../../../shared/charts/load-host-chart-renderer'
interface Props {
  name: string
  spec: Record<string, unknown>
}
export function AnalyticsChartCanvas({ name, spec }: Props) {
  const id = `pcanalytics-${useId().replaceAll(':', '')}`
  useEffect(() => {
    let active = true
    let renderer: Awaited<ReturnType<typeof loadHostChartRenderer>> | undefined
    void loadHostChartRenderer().then((loaded) => {
      renderer = loaded
      if (active) loaded.render(id, spec)
    })
    return () => {
      active = false
      renderer?.destroy(id)
    }
  }, [id, spec])
  return (
    <div className="analytics-canvas">
      <canvas aria-label={`${name} chart`} id={id} role="img" />
    </div>
  )
}
