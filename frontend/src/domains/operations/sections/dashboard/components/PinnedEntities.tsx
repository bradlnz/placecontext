import type { DashboardEntity } from '../../../model/dashboard'

interface PinnedEntitiesProps {
  entities: DashboardEntity[]
}

export function PinnedEntities({ entities }: PinnedEntitiesProps) {
  if (entities.length === 0) return null

  return (
    <section aria-label="Pinned entities" className="dashboard-entity-grid">
      {entities.map((entity) => (
        <a
          className="dccard dashboard-entity-card"
          href={`/project/${entity.projectId}/entity/${encodeURIComponent(entity.name)}`}
          key={entity.id}
        >
          <div className="entity-head">
            <span className="entity-name">{entity.name}</span>
            <span className="entity-count">{entity.rowCount?.toLocaleString() ?? '—'}</span>
          </div>
          {entity.chartColumn !== null && entity.bars.length > 0 ? (
            <>
              <div className="entity-chart-label">by {entity.chartColumn}</div>
              <div className="entity-bars">
                {entity.bars.map((bar) => (
                  <div className="entity-bar-row" key={bar.label}>
                    <span className="entity-bar-label" title={bar.label}>
                      {bar.label}
                    </span>
                    <span className="entity-bar-track">
                      <span
                        className="entity-bar-fill"
                        style={{ width: `${String(bar.percentage)}%` }}
                      />
                    </span>
                    <span className="entity-bar-count">{bar.count}</span>
                  </div>
                ))}
              </div>
            </>
          ) : (
            <div className="entity-table">{entity.tableName}</div>
          )}
        </a>
      ))}
    </section>
  )
}
