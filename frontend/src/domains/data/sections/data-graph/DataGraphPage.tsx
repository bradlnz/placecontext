import { useSuspenseQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'

import { DataTabs } from '../../../../shared/components/data-tabs/DataTabs'
import { projectGraphQueryOptions } from '../../api/project-graph-query'
import type { ProjectGraphNode } from '../../model/project-graph'
import { ProjectGraphCanvas } from './ProjectGraphCanvas'

function shortLabel(label: string): string {
  const name = label.slice(label.lastIndexOf('/') + 1)
  return name.length > 40 ? `${name.slice(0, 39)}…` : name
}

function kindClass(kind: string | null): string {
  return kind === null || kind.trim() === '' ? 'kind-default' : `kind-${kind.toLocaleLowerCase()}`
}

export function DataGraphPage() {
  const { projectId = '' } = useParams<{ projectId: string }>()
  const { data: graph } = useSuspenseQuery(projectGraphQueryOptions(projectId))
  const [search, setSearch] = useState('')
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const filteredNodes = useMemo(() => {
    let nodes: ProjectGraphNode[] = graph.nodes
    if (selectedNodeId !== null) {
      const neighborIds = new Set([selectedNodeId])
      for (const link of graph.links) {
        if (link.source === selectedNodeId) neighborIds.add(link.target)
        else if (link.target === selectedNodeId) neighborIds.add(link.source)
      }
      nodes = nodes.filter((node) => neighborIds.has(node.id))
    }
    const term = search.trim().toLocaleLowerCase()
    if (term !== '')
      nodes = nodes.filter(
        (node) =>
          node.label.toLocaleLowerCase().includes(term) ||
          node.content?.toLocaleLowerCase().includes(term) === true ||
          node.id.toLocaleLowerCase().includes(term),
      )
    return [...nodes].sort(
      (left, right) =>
        Number(right.id === selectedNodeId) - Number(left.id === selectedNodeId) ||
        right.degree - left.degree ||
        left.label.localeCompare(right.label),
    )
  }, [graph.links, graph.nodes, search, selectedNodeId])

  async function selectNode(nodeId: string | null): Promise<void> {
    await Promise.resolve()
    setSelectedNodeId(nodeId)
  }

  async function clearSelection(): Promise<void> {
    await Promise.resolve()
    setSelectedNodeId(null)
    setSearch('')
  }

  return (
    <div className="data-graph-page">
      <title>PlaceContext — Graph</title>
      <DataTabs active="graph" projectId={projectId} />
      {graph.nodes.length === 0 ? (
        <div className="dccard data-graph-empty">
          No graph yet — record decisions, run jobs and ingest data to build the project knowledge
          graph.
        </div>
      ) : (
        <div className="data-graph-studio">
          <aside className="data-graph-sidebar">
            <header>
              <input
                className="dcinput"
                onChange={(event) => {
                  setSearch(event.target.value)
                }}
                placeholder="Search nodes…"
                value={search}
              />
              {selectedNodeId === null ? null : (
                <button className="dcbtn" onClick={() => void clearSelection()} type="button">
                  Show all
                </button>
              )}
            </header>
            <div className="data-graph-node-list">
              {filteredNodes.map((node) => (
                <button
                  className={
                    node.id === selectedNodeId ? 'data-graph-node active' : 'data-graph-node'
                  }
                  key={node.id}
                  onClick={() => void selectNode(node.id)}
                  title={node.label}
                  type="button"
                >
                  <span className={`data-graph-node-kind ${kindClass(node.kind)}`} />
                  <span>{shortLabel(node.label)}</span>
                  <small>{node.degree}</small>
                </button>
              ))}
            </div>
            <footer>
              <span>{graph.nodes.length} nodes</span>
              <span>{graph.links.length} links</span>
            </footer>
          </aside>
          <main className="data-graph-main">
            <ProjectGraphCanvas
              graph={graph}
              onNodeSelect={(nodeId) => {
                setSelectedNodeId(nodeId)
              }}
              selectedNodeId={selectedNodeId}
            />
          </main>
        </div>
      )}
    </div>
  )
}
