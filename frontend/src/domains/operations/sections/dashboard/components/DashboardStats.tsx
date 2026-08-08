import type { DashboardStatsModel } from '../../../model/dashboard'

interface DashboardStatsProps {
  stats: DashboardStatsModel
}

const STAT_DEFINITIONS = [
  { key: 'running', label: 'RUNNING', unit: 'jobs', tone: 'good' },
  { key: 'queued', label: 'QUEUED', unit: 'waiting to start', tone: 'warn' },
  { key: 'failed24', label: 'FAILED · 24H', unit: 'needs attention', tone: 'bad' },
  { key: 'succeeded24', label: 'SUCCEEDED · 24H', unit: 'artifacts generated', tone: 'text' },
] as const

export function DashboardStats({ stats }: DashboardStatsProps) {
  return (
    <section className="dashboard-stat-grid" aria-label="Job statistics">
      {STAT_DEFINITIONS.map((definition) => (
        <article className="dccard dashboard-stat-card" key={definition.key}>
          <div className="dashboard-stat-label">{definition.label}</div>
          <div className="dashboard-stat-figure">
            <span className={`dashboard-stat-number tone-${definition.tone}`}>
              {stats[definition.key]}
            </span>
            <span className="dashboard-stat-unit">{definition.unit}</span>
          </div>
        </article>
      ))}
    </section>
  )
}
