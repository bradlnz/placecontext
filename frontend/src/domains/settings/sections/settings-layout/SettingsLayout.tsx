import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'

const SETTINGS_ITEMS = [
  ['Branding', 'branding'],
  ['Access', 'access'],
  ['API tokens', 'api-tokens'],
  ['Artifacts', 'artifacts'],
  ['Backup', 'backup'],
  ['Communications', 'communications'],
  ['Connections', 'connections'],
  ['Locality', 'locality'],
  ['MCP', 'mcp'],
  ['Menu', 'menu'],
] as const

const MIGRATED_SETTINGS = new Set(['api-tokens', 'artifacts', 'backup', 'branding', 'communications', 'locality', 'menu'])

export function SettingsLayout() {
  const [navigationOpen, setNavigationOpen] = useState(false)

  async function handleNavigationToggle(): Promise<void> {
    await Promise.resolve()
    setNavigationOpen((current) => !current)
  }

  async function handleNavigationClose(): Promise<void> {
    await Promise.resolve()
    setNavigationOpen(false)
  }

  return (
    <div className="settings-shell">
      <button aria-controls="settings-sections" aria-expanded={navigationOpen} aria-label="Settings sections" className="settings-toggle" onClick={() => void handleNavigationToggle()} type="button">
        <span>Settings</span><span className="settings-current">Sections</span><span aria-hidden="true">{navigationOpen ? '−' : '+'}</span>
      </button>
      <nav aria-label="Settings sections" className={`settings-nav${navigationOpen ? ' open' : ''}`} id="settings-sections">
        <div className="settings-nav-head">Settings</div>
        {SETTINGS_ITEMS.map(([label, path]) => MIGRATED_SETTINGS.has(path) ? (
          <NavLink className="settings-link" key={path} onClick={() => void handleNavigationClose()} to={`/settings/${path}`}>{label}</NavLink>
        ) : <a className="settings-link" href={`/settings/${path}`} key={path}>{label}</a>)}
      </nav>
      <div className="settings-body"><Outlet /></div>
    </div>
  )
}
