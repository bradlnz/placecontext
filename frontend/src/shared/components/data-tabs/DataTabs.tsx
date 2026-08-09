import { NavLink } from 'react-router-dom'

interface DataTabsProps {
  active: 'records' | 'search' | 'analytics' | 'data-map' | 'entities' | 'graph'
  projectId: string
}

const tabs = [
  { key: 'records', label: 'Records', segment: 'data' },
  { key: 'search', label: 'Search', segment: 'data-search' },
  {
    key: 'analytics',
    label: 'Analytics',
    segment: 'analytics',
  },
  { key: 'data-map', label: 'Data map', segment: 'datamap' },
  { key: 'entities', label: 'Entities', segment: 'entities' },
  { key: 'graph', label: 'Graph', segment: 'data-graph' },
] as const

export function DataTabs({ active, projectId }: DataTabsProps) {
  return (
    <nav aria-label="Project data" className="dctabs data-tabs">
      {tabs.map((tab) => (
        <NavLink
          className={tab.key === active ? 'dctab active' : 'dctab'}
          key={tab.key}
          to={`/project/${projectId}/${tab.segment}`}
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  )
}
