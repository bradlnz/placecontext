import type { WorkspaceStats as WorkspaceStatsModel } from '../../../model/workspace'

interface WorkspaceStatsProps {
  stats: WorkspaceStatsModel
}

export function WorkspaceStats({ stats }: WorkspaceStatsProps) {
  const statItems = [
    {
      label: 'Projects',
      value: stats.projectCount,
      detail: 'under root',
      color: 'var(--text)',
    },
    {
      label: 'Changes today',
      value: stats.changesToday,
      detail: `${String(stats.agentChangesToday)} agent · ${String(stats.humanChangesToday)} human`,
      color: 'var(--text)',
    },
    {
      label: 'God-nodes',
      value: stats.godNodeTotal,
      detail: 'top-degree files',
      color: 'var(--text)',
    },
    {
      label: 'Stale context',
      value: stats.staleContextCount,
      detail: stats.staleContextCount > 0 ? 'need re-index' : 'all current',
      color: stats.staleContextCount > 0 ? 'var(--warn)' : 'var(--good)',
    },
  ] as const

  return (
    <section className="stat-strip" aria-label="Workspace statistics">
      {statItems.map((item) => (
        <article className="stat-cell" key={item.label}>
          <div className="stat-label">{item.label}</div>
          <div className="stat-value" style={{ color: item.color }}>
            {item.value.toLocaleString()}
          </div>
          <div className="stat-sub">{item.detail}</div>
        </article>
      ))}
    </section>
  )
}
