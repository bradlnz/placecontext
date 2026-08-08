import { useAppEventBus } from '../../../../app/app-event-bus'
import { FocusPanel } from './components/FocusPanel'
import { ProjectGrid } from './components/ProjectGrid'
import { WorkspaceStats } from './components/WorkspaceStats'
import { useWorkspaceOverview } from './overview-query'

export function OverviewPage() {
  const eventBus = useAppEventBus()
  const { data, isRefreshing } = useWorkspaceOverview()

  async function handleRefresh(): Promise<void> {
    await eventBus.publish('workspace.overview-refresh-requested', {
      source: 'overview-header',
    })
  }

  return (
    <div className="page">
      <title>PlaceContext — Overview</title>
      <WorkspaceStats stats={data.stats} />
      <FocusPanel focus={data.focus} />

      <section aria-labelledby="projects-title">
        <div className="projects-head">
          <div className="projects-title-group">
            <h2 className="projects-title" id="projects-title">
              Projects
            </h2>
            <span className="projects-count">{data.projects.length}</span>
          </div>
          <div className="projects-actions">
            <button
              className="dcbtn primary"
              disabled={isRefreshing}
              onClick={() => void handleRefresh()}
              type="button"
            >
              {isRefreshing ? 'Refreshing…' : 'Refresh'}
            </button>
          </div>
        </div>

        <ProjectGrid projects={data.projects} />
      </section>
    </div>
  )
}
