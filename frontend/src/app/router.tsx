import { createBrowserRouter, type RouteObject } from 'react-router-dom'

import { RouteErrorPage } from '../shared/components/route-error-page/RouteErrorPage'
import { SectionLoading } from '../shared/components/loading/SectionLoading'
import { AppShell } from '../domains/navigation/sections/app-shell/AppShell'
import { AuthShell } from '../domains/identity/sections/auth-shell/AuthShell'
import { SettingsLayout } from '../domains/settings/sections/settings-layout/SettingsLayout'

export const appRoutes: RouteObject[] = [
  {
    element: <AppShell />,
    errorElement: <RouteErrorPage />,
    HydrateFallback: SectionLoading,
    children: [
      {
        index: true,
        handle: {
          title: 'Dashboard',
          subtitle: 'jobs · runs · artifacts',
        },
        lazy: async () => {
          const { DashboardPage } = await import(
            '../domains/operations/sections/dashboard/DashboardPage'
          )

          return { Component: DashboardPage }
        },
      },
      {
        path: 'overview',
        handle: {
          title: 'Overview',
          subtitle: 'codebase visibility · projects register via MCP',
        },
        lazy: async () => {
          const { OverviewPage } = await import(
            '../domains/workspace/sections/overview/OverviewPage'
          )

          return { Component: OverviewPage }
        },
      },
      {
        path: 'about',
        handle: {
          title: 'About',
          subtitle: 'PlaceContext — a full-scale data platform',
        },
        lazy: async () => {
          const { AboutPage } = await import(
            '../domains/system/sections/about/AboutPage'
          )

          return { Component: AboutPage }
        },
      },
      {
        path: 'onboarding',
        lazy: async () => {
          const { OnboardingPage } = await import(
            '../domains/identity/sections/onboarding/OnboardingPage'
          )

          return { Component: OnboardingPage }
        },
      },
      {
        path: 'settings',
        element: <SettingsLayout />,
        children: [
          {
            path: 'api-tokens',
            handle: { title: 'API tokens', subtitle: 'personal tokens for project data and search' },
            lazy: async () => {
              const { ApiTokensPage } = await import('../domains/settings/sections/api-tokens/ApiTokensPage')
              return { Component: ApiTokensPage }
            },
          },
          {
            path: 'artifacts',
            handle: { title: 'Settings', subtitle: 'Artifact filters' },
            lazy: async () => {
              const { ArtifactFiltersPage } = await import('../domains/settings/sections/artifacts/ArtifactFiltersPage')
              return { Component: ArtifactFiltersPage }
            },
          },
          {
            path: 'branding',
            handle: { title: 'Branding', subtitle: 'whitelabel the portal for this workspace' },
            lazy: async () => {
              const { BrandingPage } = await import('../domains/settings/sections/branding/BrandingPage')
              return { Component: BrandingPage }
            },
          },
          {
            path: 'backup',
            handle: {
              title: 'Backup',
              subtitle: "export/import this workspace's settings and job definitions",
            },
            lazy: async () => {
              const { BackupPage } = await import(
                '../domains/settings/sections/backup/BackupPage'
              )

              return { Component: BackupPage }
            },
          },
          {
            path: 'communications',
            handle: { title: 'Settings', subtitle: 'Communications' },
            lazy: async () => {
              const { CommunicationsPage } = await import('../domains/settings/sections/communications/CommunicationsPage')
              return { Component: CommunicationsPage }
            },
          },
          {
            path: 'locality',
            handle: { title: 'Locality', subtitle: 'workspace timezone — schedules and displayed times obey it' },
            lazy: async () => {
              const { LocalityPage } = await import('../domains/settings/sections/locality/LocalityPage')
              return { Component: LocalityPage }
            },
          },
          {
            path: 'menu',
            handle: { title: 'Settings', subtitle: 'Menu' },
            lazy: async () => {
              const { MenuPage } = await import('../domains/settings/sections/menu/MenuPage')
              return { Component: MenuPage }
            },
          },
        ],
      },
    ],
  },
  {
    element: <AuthShell />,
    errorElement: <RouteErrorPage />,
    HydrateFallback: SectionLoading,
    children: [
      {
        path: 'login',
        lazy: async () => {
          const { LoginPage } = await import(
            '../domains/identity/sections/login/LoginPage'
          )

          return { Component: LoginPage }
        },
      },
      {
        path: 'setup',
        lazy: async () => {
          const { SetupPage } = await import(
            '../domains/identity/sections/setup/SetupPage'
          )

          return { Component: SetupPage }
        },
      },
    ],
  },
]

export const appRouter = createBrowserRouter(appRoutes, { basename: '/app' })
