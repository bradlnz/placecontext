import { useQuery } from '@tanstack/react-query'
import { Suspense, useEffect, useState } from 'react'
import { NavLink, Outlet, useMatches } from 'react-router-dom'

import { useAppEventBus } from '../../../../app/app-event-bus'
import { SectionLoading } from '../../../../shared/components/loading/SectionLoading'
import { navigateToLegacyProject } from '../../../../shared/navigation/legacy-navigation'
import {
  workspaceProjectsQuery,
  workspaceSessionQuery,
} from '../../../workspace/api/workspace-query-options'
import { NavigationGlyph, type NavigationIcon } from './NavigationGlyph'

interface NavigationItem {
  label: string
  href: string
  section?: string
  icon: NavigationIcon
}

const DASHBOARD_NAVIGATION: NavigationItem = { label: 'Dashboard', href: '/app/', icon: 'grid' }

const WORKSPACE_NAVIGATION: readonly NavigationItem[] = [
  { label: 'Chat', href: '/app/chat', icon: 'chat' },
  { label: 'Artifacts', href: '/app/artifacts', icon: 'file' },
  { label: 'Observability', href: '/observability', icon: 'pulse' },
  { label: 'Cluster', href: '/cluster', icon: 'box' },
  { label: 'Projects overview', href: '/app/overview', icon: 'pulse', section: 'Workspace' },
  { label: 'Wiki', href: '/wiki', icon: 'ledger', section: 'Workspace' },
  { label: 'Settings', href: '/settings/branding', icon: 'key', section: 'Workspace' },
  { label: 'About', href: '/app/about', icon: 'grid', section: 'Workspace' },
]

const PROJECT_NAVIGATION: readonly Omit<NavigationItem, 'href'>[] = [
  { label: 'CRM', icon: 'crm' },
  { label: 'Jobs', icon: 'box' },
  { label: 'Tests', icon: 'test' },
  { label: 'Chains', icon: 'chain' },
  { label: 'Schedules', icon: 'clock' },
  { label: 'Data', icon: 'data' },
  { label: 'Vault', icon: 'key' },
  { label: 'Events', icon: 'pulse' },
]

const PROJECT_PATHS: Record<string, string> = {
  CRM: 'crm',
  Jobs: 'jobs',
  Tests: 'tests',
  Chains: 'chains',
  Schedules: 'schedules',
  Data: 'data',
  Vault: 'secrets',
  Events: 'events',
}

export function AppShell() {
  const eventBus = useAppEventBus()
  const matches = useMatches()
  const projectsQuery = useQuery(workspaceProjectsQuery)
  const sessionQuery = useQuery(workspaceSessionQuery)
  const [navigationOpen, setNavigationOpen] = useState(false)
  const [switcherOpen, setSwitcherOpen] = useState(false)
  const [theme, setTheme] = useState<'dark' | 'light'>('dark')
  const [runningCount, setRunningCount] = useState(0)
  const currentProject = projectsQuery.data?.[0]
  const session = sessionQuery.data
  const pageHandle = matches.findLast((match) => {
    if (typeof match.handle !== 'object' || match.handle === null) return false
    return 'title' in match.handle && 'subtitle' in match.handle
  })?.handle as { title: string; subtitle: string } | undefined

  useEffect(() => {
    return eventBus.subscribe('workspace.project-selected', async ({ projectId }) => {
      await navigateToLegacyProject(projectId)
    })
  }, [eventBus])

  useEffect(() => {
    return eventBus.subscribe('dashboard.loaded', async ({ runningCount: nextRunningCount }) => {
      await Promise.resolve()
      setRunningCount(nextRunningCount)
    })
  }, [eventBus])

  async function handleNavigationToggle(): Promise<void> {
    await Promise.resolve()
    setNavigationOpen((current) => !current)
  }

  async function handleNavigationClose(): Promise<void> {
    await Promise.resolve()
    setNavigationOpen(false)
  }

  async function handleSwitcherToggle(): Promise<void> {
    await Promise.resolve()
    setSwitcherOpen((current) => !current)
  }

  async function handleProjectSelected(projectId: string): Promise<void> {
    setSwitcherOpen(false)
    await eventBus.publish('workspace.project-selected', { projectId })
  }

  async function handleThemeToggle(): Promise<void> {
    await Promise.resolve()
    setTheme((current) => (current === 'dark' ? 'light' : 'dark'))
  }

  const projectNavigation =
    currentProject === undefined
      ? []
      : PROJECT_NAVIGATION.map((item) => ({
          ...item,
          href: `/project/${currentProject.id}/${PROJECT_PATHS[item.label] ?? ''}`,
        }))
  const navigation = [DASHBOARD_NAVIGATION, ...projectNavigation, ...WORKSPACE_NAVIGATION]

  return (
    <div id="dcshell" data-theme={theme} className="shell">
      <aside
        className={navigationOpen ? 'sidebar open' : 'sidebar'}
        aria-label="Primary navigation"
      >
        <div className="brand-row">
          <div className="brand-mark" aria-hidden="true">
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="#fff"
              strokeWidth="2.1"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M5 8l3.5 4L5 16" />
              <path d="M13 16h6" />
            </svg>
          </div>
          <div className="brand-text">
            <span className="title-14">placecontext</span>
            <span className="org-name">{session?.tenant ?? 'organisation'}</span>
          </div>
          <button
            className="sidebar-close"
            onClick={() => void handleNavigationClose()}
            aria-label="Close navigation"
            type="button"
          >
            ×
          </button>
        </div>

        <nav className="nav-list" aria-label="Workspace menu">
          {navigation.map((item, index) => {
            const previousItem = navigation[index - 1]
            const showSection = item.section !== undefined && item.section !== previousItem?.section
            const itemContent = (
              <>
                <span className="bar" />
                <NavigationGlyph kind={item.icon} />
                <span className="nav-label">{item.label}</span>
              </>
            )

            return (
              <div key={item.label}>
                {showSection ? <div className="nav-section">{item.section}</div> : null}
                {item.href.startsWith('/app/') ? (
                  <NavLink className="dcnav" to={item.href.slice('/app'.length)} end>
                    {itemContent}
                  </NavLink>
                ) : (
                  <a className="dcnav" href={item.href}>
                    {itemContent}
                  </a>
                )}
              </div>
            )
          })}
        </nav>

        <div className="user-bar">
          <div className="user-info">
            <div className="user-avatar">
              {session?.displayName.slice(0, 1).toUpperCase() ?? 'P'}
            </div>
            <div className="user-detail">
              <div className="user-name">{session?.displayName ?? 'PlaceContext user'}</div>
              <div className="user-role">{session?.role ?? 'Viewer'}</div>
            </div>
          </div>
        </div>
      </aside>

      {navigationOpen ? (
        <button
          aria-label="Close navigation"
          className="nav-backdrop"
          onClick={() => void handleNavigationClose()}
          type="button"
        />
      ) : null}

      <main className="main">
        <header className="topbar">
          <div className="topbar-left">
            <button
              className="nav-toggle"
              onClick={() => void handleNavigationToggle()}
              title="Menu"
              aria-label="Toggle navigation"
              type="button"
            >
              ☰
            </button>
            <span className="title-14">{pageHandle?.title ?? 'PlaceContext'}</span>
            <span className="topbar-sub">{pageHandle?.subtitle ?? 'workspace'}</span>
          </div>
          <div className="topbar-right">
            <a className="dcsearch-trigger" href="/inspector">
              <span aria-hidden="true">⌕</span>
              <span className="dcsearch-text">Search context, files, changes</span>
              <kbd>/</kbd>
            </a>
            <div className="running-badge">
              <span className="running-dot" />
              {runningCount} running
            </div>
            <a className="icon-btn" href="/observability" aria-label="Notifications">
              ♢
            </a>
            <div className="topbar-switcher-wrap switcher-wrap">
              <button
                className="topbar-switcher-pill switcher-pill"
                onClick={() => void handleSwitcherToggle()}
                type="button"
              >
                <span className="switcher-dot" />
                <span className="topbar-switcher-name switcher-name">
                  {currentProject?.name ?? 'select project'}
                </span>
                <span className="topbar-switcher-caret switcher-caret">▾</span>
              </button>
              {switcherOpen ? (
                <div className="switcher-menu">
                  {(projectsQuery.data ?? []).map((project) => (
                    <button
                      className="switcher-item"
                      key={project.id}
                      onClick={() => void handleProjectSelected(project.id)}
                      type="button"
                    >
                      <span className="switcher-item-dot" />
                      <span className="switcher-item-name">{project.name}</span>
                    </button>
                  ))}
                </div>
              ) : null}
            </div>
            <button
              className="icon-btn"
              onClick={() => void handleThemeToggle()}
              aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
              type="button"
            >
              ◐
            </button>
          </div>
        </header>

        <div className="body-scroll">
          <Suspense fallback={<SectionLoading />}>
            <Outlet />
          </Suspense>
        </div>
      </main>
    </div>
  )
}
