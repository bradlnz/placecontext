import { NavLink } from 'react-router-dom'

interface DataTabsProps {
  active: 'records' | 'analytics' | 'data-map' | 'entities' | 'graph'
  projectId: string
}

const tabs = [
  { key: 'records', label: 'Records', segment: 'data', migrated: false },
  {
    key: 'analytics',
    label: 'Analytics',
    segment: 'analytics',
    migrated: true,
  },
  { key: 'data-map', label: 'Data map', segment: 'datamap', migrated: true },
  { key: 'entities', label: 'Entities', segment: 'entities', migrated: true },
  { key: 'graph', label: 'Graph', segment: 'data-graph', migrated: true },
] as const

export function DataTabs({ active, projectId }: DataTabsProps) {
  return (
    <nav aria-label="Project data" className="dctabs data-tabs">
      {tabs.map((tab) =>
        tab.migrated ? (
          <NavLink
            className={tab.key === active ? 'dctab active' : 'dctab'}
            key={tab.key}
            to={`/project/${projectId}/${tab.segment}`}
          >
            {tab.label}
          </NavLink>
        ) : (
          <a
            className={tab.key === active ? 'dctab active' : 'dctab'}
            href={`/project/${projectId}/${tab.segment}`}
            key={tab.key}
          >
            {tab.label}
          </a>
        ),
      )}
    </nav>
  )
}
