import type { ProjectGraph } from '../../domains/data/model/project-graph'

interface PcGraphCallback {
  invokeMethodAsync(method: 'OnNodeClick', nodeId: string | null): Promise<void>
}

interface PcGraphApi {
  destroy(id: string): void
  init(id: string, graph: PcGraphPayload, callback: PcGraphCallback): void
  select(id: string, nodeId: string | null): void
}

interface PcGraphPayload {
  nodes: {
    id: string
    label: string
    degree: number
    god: boolean
    kind: string | null
    labeled: boolean
  }[]
  links: { source: string; target: string; confidence: string }[]
}

declare global {
  interface Window {
    pcgraph?: PcGraphApi
  }
}

let graphScriptPromise: Promise<PcGraphApi> | null = null

export function graphPayload(graph: ProjectGraph): PcGraphPayload {
  return {
    nodes: graph.nodes.slice(0, 180).map((node) => ({
      id: node.id,
      label: node.label,
      degree: node.degree,
      god: node.isGod,
      kind: node.kind,
      labeled: node.labeled,
    })),
    links: graph.links.slice(0, 500),
  }
}

export function loadPcGraph(): Promise<PcGraphApi> {
  if (window.pcgraph !== undefined) return Promise.resolve(window.pcgraph)
  graphScriptPromise ??= new Promise<PcGraphApi>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = '/pcgraph.js?v=3'
    script.async = true
    script.addEventListener(
      'load',
      () => {
        if (window.pcgraph === undefined)
          reject(new Error('The graph renderer did not initialise.'))
        else resolve(window.pcgraph)
      },
      { once: true },
    )
    script.addEventListener(
      'error',
      () => {
        reject(new Error('The graph renderer could not be loaded.'))
      },
      { once: true },
    )
    document.head.append(script)
  })
  return graphScriptPromise
}
