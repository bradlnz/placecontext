import { useEffect, useId, useMemo } from 'react'

import { loadHostChartRenderer } from '../../../../shared/charts/load-host-chart-renderer'

export function OpenSearchChart({ specJson }: { specJson: string }) {
  const generatedId = useId()
  const canvasId = `open-search-react-${generatedId.replaceAll(':', '')}`
  const spec = useMemo(() => JSON.parse(specJson) as Record<string, unknown>, [specJson])

  useEffect(() => {
    let renderer: Awaited<ReturnType<typeof loadHostChartRenderer>> | undefined
    void loadHostChartRenderer().then((loaded) => {
      renderer = loaded
      loaded.render(canvasId, spec)
    })
    return () => {
      renderer?.destroy(canvasId)
    }
  }, [canvasId, spec])

  return <canvas id={canvasId} />
}
