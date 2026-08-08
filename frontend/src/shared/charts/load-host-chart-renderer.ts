interface HostChartRenderer {
  render: (id: string, spec: Record<string, unknown>) => void
  destroy: (id: string) => void
}

let rendererPromise: Promise<HostChartRenderer> | undefined

async function loadScript(path: string): Promise<void> {
  const existing = document.querySelector<HTMLScriptElement>(`script[src="${path}"]`)
  if (existing?.dataset.loaded === 'true') return

  await new Promise<void>((resolve, reject) => {
    const script = existing ?? document.createElement('script')
    const handleLoaded = () => {
      script.dataset.loaded = 'true'
      resolve()
    }
    const handleError = () => {
      reject(new Error(`Unable to load ${path}.`))
    }

    script.addEventListener('load', handleLoaded, { once: true })
    script.addEventListener('error', handleError, { once: true })
    if (existing === null) {
      script.src = path
      script.async = true
      document.head.append(script)
    }
  })
}

export async function loadHostChartRenderer(): Promise<HostChartRenderer> {
  if (window.pcchart !== undefined) return window.pcchart

  rendererPromise ??= (async () => {
    await loadScript('/chart.umd.js')
    await loadScript('/pcchart.js')
    if (window.pcchart === undefined) {
      throw new Error('The PlaceContext chart renderer did not initialise.')
    }

    return window.pcchart
  })()

  return rendererPromise
}
