import { useAppEventBus } from '../../../../../app/app-event-bus'
import type { WorkspaceProject } from '../../../model/workspace'
import { ProjectMinimap } from './ProjectMinimap'

interface ProjectGridProps {
  projects: WorkspaceProject[]
}

export function ProjectGrid({ projects }: ProjectGridProps) {
  const eventBus = useAppEventBus()

  async function handleProjectSelected(projectId: string): Promise<void> {
    await eventBus.publish('workspace.project-selected', { projectId })
  }

  if (projects.length === 0) {
    return (
      <div className="dccard empty-card">
        No projects yet. Projects register themselves through the MCP <code>create_project</code> tool when an agent starts working in a repo.
      </div>
    )
  }

  return (
    <div className="projects-grid">
      {projects.map((project) => (
        <button
          aria-label={`Open ${project.name}`}
          className="dccard project-card"
          key={project.id}
          onClick={() => void handleProjectSelected(project.id)}
          type="button"
        >
          <div className="project-card-head">
            <span className="min-w-0">
              <span className="project-name-row">
                <span className="lang-dot" aria-hidden="true" />
                <span className="project-name">{project.name}</span>
              </span>
              <span className="project-path" title={project.path}>{project.path}</span>
            </span>
            <span className="status-badge">
              <span className="status-dot" aria-hidden="true" />
              {project.status}
            </span>
          </div>

          <div className="map-row"><ProjectMinimap project={project} /></div>

          <div className="project-foot">
            <span className="foot-graphified">
              {project.isGraphified ? 'graphified' : 'not graphified'}
            </span>
            <span className="foot-stats">
              <span>{project.nodeCount.toLocaleString()} nodes</span>
              <span className="foot-sep">·</span>
              <span>{project.linkCount.toLocaleString()} links</span>
            </span>
          </div>
        </button>
      ))}
    </div>
  )
}
