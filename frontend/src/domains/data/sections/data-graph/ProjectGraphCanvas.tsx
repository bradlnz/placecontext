import { useEffect, useId, useMemo, useRef, useState } from 'react'

import { graphPayload, loadPcGraph } from '../../../../shared/graph/pcgraph'
import type { ProjectGraph } from '../../model/project-graph'

interface ProjectGraphCanvasProps {
  graph: ProjectGraph
  onNodeSelect: (nodeId: string | null) => void
  selectedNodeId: string | null
}

function shortLabel(label: string): string {
  const name = label.slice(label.lastIndexOf('/') + 1)
  return name.length > 40 ? `${name.slice(0, 39)}…` : name
}

export function ProjectGraphCanvas({
  graph,
  onNodeSelect,
  selectedNodeId,
}: ProjectGraphCanvasProps) {
  const reactId = useId()
  const canvasId = `pcgraph-react-${reactId.replaceAll(':', '')}`
  const onNodeSelectRef = useRef(onNodeSelect)
  const selectedNodeIdRef = useRef(selectedNodeId)
  const [search, setSearch] = useState('')
  const [fullscreen, setFullscreen] = useState(false)
  const [rendererError, setRendererError] = useState<string | null>(null)
  const selected = graph.nodes.find((node) => node.id === selectedNodeId) ?? null
  const matches = useMemo(() => {
    const term = search.trim().toLocaleLowerCase()
    if (term.length < 2) return []
    return graph.nodes
      .filter(
        (node) =>
          node.label.toLocaleLowerCase().includes(term) ||
          node.content?.toLocaleLowerCase().includes(term) === true,
      )
      .sort((left, right) => right.degree - left.degree)
      .slice(0, 8)
  }, [graph.nodes, search])
  const neighbors = useMemo(() => {
    if (selected === null) return []
    const ids = graph.links
      .filter((link) => link.source === selected.id || link.target === selected.id)
      .map((link) => ({
        id: link.source === selected.id ? link.target : link.source,
        confidence: link.confidence,
      }))
    return ids
      .map((item) => ({
        node: graph.nodes.find((node) => node.id === item.id),
        confidence: item.confidence,
      }))
      .filter((item) => item.node !== undefined)
      .sort((left, right) => (right.node?.degree ?? 0) - (left.node?.degree ?? 0))
  }, [graph.links, graph.nodes, selected])

  useEffect(() => {
    onNodeSelectRef.current = onNodeSelect
    selectedNodeIdRef.current = selectedNodeId
  }, [onNodeSelect, selectedNodeId])

  useEffect(() => {
    let active = true
    const callback = {
      invokeMethodAsync: async (_method: 'OnNodeClick', nodeId: string | null) => {
        await Promise.resolve()
        if (active) onNodeSelectRef.current(nodeId)
      },
    }
    void loadPcGraph()
      .then((api) => {
        if (!active) return
        api.init(canvasId, graphPayload(graph), callback)
        if (selectedNodeIdRef.current !== null) api.select(canvasId, selectedNodeIdRef.current)
      })
      .catch((error: unknown) => {
        if (active)
          setRendererError(
            error instanceof Error ? error.message : 'The graph renderer could not be loaded.',
          )
      })
    return () => {
      active = false
      window.pcgraph?.destroy(canvasId)
    }
  }, [canvasId, graph])

  useEffect(() => {
    window.pcgraph?.select(canvasId, selectedNodeId)
  }, [canvasId, selectedNodeId])

  async function selectMatch(nodeId: string): Promise<void> {
    setSearch('')
    await loadPcGraph().then((api) => {
      api.select(canvasId, nodeId)
    })
  }

  async function toggleFullscreen(): Promise<void> {
    await Promise.resolve()
    setFullscreen((current) => !current)
  }

  async function clearSelection(): Promise<void> {
    await Promise.resolve()
    onNodeSelect(null)
  }

  return (
    <div className={fullscreen ? 'project-graph-canvas fullscreen' : 'project-graph-canvas'}>
      <div className="project-graph-chrome">
        <div className="project-graph-search-wrap">
          <input
            className="dcinput"
            onChange={(event) => {
              setSearch(event.target.value)
            }}
            placeholder="search nodes…"
            value={search}
          />
          {matches.length === 0 ? null : (
            <div className="project-graph-search-results">
              {matches.map((node) => (
                <button key={node.id} onClick={() => void selectMatch(node.id)} type="button">
                  <strong>{shortLabel(node.label)}</strong>
                  {node.content === null ? null : <small>{node.content}</small>}
                </button>
              ))}
            </div>
          )}
        </div>
        <button className="dcbtn" onClick={() => void toggleFullscreen()} type="button">
          {fullscreen ? '✕ Exit full screen' : '⛶ Full screen'}
        </button>
      </div>
      {rendererError === null ? null : (
        <div className="error-banner" role="alert">
          {rendererError}
        </div>
      )}
      <canvas className="project-graph-canvas-element" id={canvasId} />
      {selected === null ? null : (
        <aside className="dccard project-graph-detail">
          <header>
            <div>
              <strong>{shortLabel(selected.label)}</strong>
              <small>{selected.id}</small>
            </div>
            <button className="dcbtn" onClick={() => void clearSelection()} type="button">
              ×
            </button>
          </header>
          <div className="project-graph-badges">
            {selected.isGod ? <span>project hub</span> : null}
            <small>{selected.degree} connection(s)</small>
          </div>
          {selected.content === null ? null : <p>{selected.content}</p>}
          {neighbors.length === 0 ? null : (
            <>
              <h2>Connected to</h2>
              <div className="project-graph-neighbors">
                {neighbors.map(({ node, confidence }) =>
                  node === undefined ? null : (
                    <button key={node.id} onClick={() => void selectMatch(node.id)} type="button">
                      {shortLabel(node.label)}
                      {confidence === 'Ambiguous' ? ' ⇢' : ''}
                    </button>
                  ),
                )}
              </div>
            </>
          )}
        </aside>
      )}
    </div>
  )
}
