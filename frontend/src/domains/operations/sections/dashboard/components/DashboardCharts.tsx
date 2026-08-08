import type { DashboardChart as DashboardChartModel, DashboardProject } from '../../../model/dashboard'
import { DashboardChart } from './DashboardChart'

interface DashboardChartsProps {
  charts: DashboardChartModel[]
  project: DashboardProject | null
}

export function DashboardCharts({ charts, project }: DashboardChartsProps) {
  if (charts.length === 0) return null

  return (
    <section aria-labelledby="dashboard-charts-title">
      <div className="dashboard-charts-head">
        <h2 className="section-title" id="dashboard-charts-title">Charts</h2>
        {project === null ? null : <a className="charts-edit-link" href={`/project/${project.id}/analytics`}>edit in Analytics →</a>}
      </div>
      <div className="dashboard-charts-grid">
        {charts.map((chart) => <DashboardChart chart={chart} key={chart.name} />)}
      </div>
    </section>
  )
}
