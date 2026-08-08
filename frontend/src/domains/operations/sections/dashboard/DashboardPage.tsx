import { DashboardCharts } from './components/DashboardCharts'
import { DashboardStats } from './components/DashboardStats'
import { PinnedEntities } from './components/PinnedEntities'
import { QuickChains } from './components/QuickChains'
import { RecentJobs } from './components/RecentJobs'
import { useDashboard } from './use-dashboard'

export function DashboardPage() {
  const {
    dashboard,
    chainMessage,
    chainError,
    runningChainId,
  } = useDashboard()

  return (
    <div className="dashboard-page">
      <title>placecontext — Dashboard</title>
      <header className="dashboard-page-head">
        <div>
          <h1>Dashboard</h1>
          <p>Jobs across <strong>{dashboard.project?.name ?? 'the workspace'}</strong> · every run yields an artifact</p>
        </div>
      </header>

      <DashboardStats stats={dashboard.stats} />
      <QuickChains
        chains={dashboard.chains}
        hasError={chainError}
        message={chainMessage}
        project={dashboard.project}
        runningChainId={runningChainId}
      />
      <PinnedEntities entities={dashboard.entities} />
      <DashboardCharts charts={dashboard.charts} project={dashboard.project} />
      <RecentJobs runs={dashboard.recentRuns} />
    </div>
  )
}
