import type { WorkspaceProject } from '../../../model/workspace'

interface ProjectMinimapProps {
  project: WorkspaceProject
}

interface MiniNode {
  id: number
  x: number
  y: number
  important: boolean
}

function hashId(id: string): number {
  let hash = 2166136261

  for (const character of id) {
    hash ^= character.charCodeAt(0)
    hash = Math.imul(hash, 16777619)
  }

  return hash >>> 0
}

function createMiniNodes(project: WorkspaceProject): MiniNode[] {
  const nodeCount = Math.min(22, project.godNodeCount * 3 + 10)
  let seed = hashId(project.id)
  const nodes: MiniNode[] = []

  for (let index = 0; index < nodeCount; index += 1) {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    const x = 8 + (seed % 84)
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0
    const y = 8 + (seed % 60)
    nodes.push({ id: index, x, y, important: index < project.godNodeCount })
  }

  return nodes
}

export function ProjectMinimap({ project }: ProjectMinimapProps) {
  const nodes = createMiniNodes(project)

  return (
    <div className="minimap" aria-hidden="true">
      <svg viewBox="0 0 100 76" preserveAspectRatio="none">
        {nodes.map((node) => (
          <circle
            className={node.important ? 'minimap-node minimap-node--important' : 'minimap-node'}
            cx={node.x}
            cy={node.y}
            key={node.id}
            r={node.important ? 2.8 : 1.25}
          />
        ))}
      </svg>
      <span className="minimap-badge">{project.godNodeCount} god</span>
    </div>
  )
}
