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
          const { DashboardPage } =
            await import('../domains/operations/sections/dashboard/DashboardPage')

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
          const { OverviewPage } =
            await import('../domains/workspace/sections/overview/OverviewPage')

          return { Component: OverviewPage }
        },
      },
      {
        path: 'inspector',
        handle: {
          title: 'MCP Inspector',
          subtitle: 'live tool traffic · MCP via Streamable HTTP',
        },
        lazy: async () => {
          const { InspectorPage } =
            await import('../domains/workspace/sections/inspector/InspectorPage')

          return { Component: InspectorPage }
        },
      },
      {
        path: 'project/:projectId/secrets',
        handle: {
          title: 'Vault',
          subtitle: 'encrypted project secrets',
        },
        lazy: async () => {
          const { VaultPage } = await import('../domains/security/sections/vault/VaultPage')

          return { Component: VaultPage }
        },
      },
      {
        path: 'project/:projectId',
        handle: {
          title: 'Project',
          subtitle: 'overview · requirements · activity',
        },
        lazy: async () => {
          const { ProjectPage } = await import('../domains/projects/sections/project/ProjectPage')

          return { Component: ProjectPage }
        },
      },
      {
        path: 'project/:projectId/data-graph',
        handle: {
          title: 'Graph',
          subtitle: 'project knowledge graph',
        },
        lazy: async () => {
          const { DataGraphPage } =
            await import('../domains/data/sections/data-graph/DataGraphPage')

          return { Component: DataGraphPage }
        },
      },
      {
        path: 'project/:projectId/datamap',
        handle: {
          title: 'Data map',
          subtitle: 'Job results → project tables',
        },
        lazy: async () => {
          const { DataMapPage } = await import('../domains/data/sections/data-map/DataMapPage')

          return { Component: DataMapPage }
        },
      },
      {
        path: 'project/:projectId/entities',
        handle: {
          title: 'Entities',
          subtitle: 'business views · relations · linked values',
        },
        lazy: async () => {
          const { EntitiesPage } = await import('../domains/data/sections/entities/EntitiesPage')

          return { Component: EntitiesPage }
        },
      },
      {
        path: 'project/:projectId/entity/:entityName',
        handle: {
          title: 'Entity records',
          subtitle: 'search · edit · linked records',
        },
        lazy: async () => {
          const { EntityBrowsePage } =
            await import('../domains/data/sections/entity-browse/EntityBrowsePage')

          return { Component: EntityBrowsePage }
        },
      },
      {
        path: 'project/:projectId/analytics',
        handle: {
          title: 'Analytics',
          subtitle: "charts over the project's data",
        },
        lazy: async () => {
          const { AnalyticsPage } = await import('../domains/data/sections/analytics/AnalyticsPage')
          return { Component: AnalyticsPage }
        },
      },
      {
        path: 'project/:projectId/schedules',
        handle: { title: 'Schedules', subtitle: 'cron · events · launchpads' },
        lazy: async () => {
          const { SchedulesPage } =
            await import('../domains/automation/sections/schedules/SchedulesPage')
          return { Component: SchedulesPage }
        },
      },
      {
        path: 'project/:projectId/jobs',
        handle: { title: 'Jobs', subtitle: 'workloads · runs · triggers' },
        lazy: async () => {
          const { JobsPage } = await import('../domains/automation/sections/jobs/JobsPage')
          return { Component: JobsPage }
        },
      },
      {
        path: 'project/:projectId/jobs/:jobId',
        handle: { title: 'Job editor', subtitle: 'multi-file workload code' },
        lazy: async () => {
          const { JobCodeEditorPage } =
            await import('../domains/automation/sections/job-editor/JobCodeEditorPage')
          return { Component: JobCodeEditorPage }
        },
      },
      {
        path: 'project/:projectId/chains',
        handle: {
          title: 'Job chains',
          subtitle: 'ordered pipelines · parallel stages',
        },
        lazy: async () => {
          const { ChainsPage } = await import('../domains/automation/sections/chains/ChainsPage')
          return { Component: ChainsPage }
        },
      },
      {
        path: 'project/:projectId/tests',
        handle: { title: 'Tests', subtitle: 'verify Job code' },
        lazy: async () => {
          const { TestsPage } = await import('../domains/automation/sections/tests/TestsPage')
          return { Component: TestsPage }
        },
      },
      {
        path: 'project/:projectId/tests/:testId',
        handle: { title: 'Test code', subtitle: 'isolated framework tests' },
        lazy: async () => {
          const { TestCodeEditorPage } =
            await import('../domains/automation/sections/test-editor/TestCodeEditorPage')
          return { Component: TestCodeEditorPage }
        },
      },
      {
        path: 'project/:projectId/events',
        handle: {
          title: 'Events',
          subtitle: 'activity log · event types · Job triggers',
        },
        lazy: async () => {
          const { EventsPage } = await import('../domains/events/sections/events/EventsPage')

          return { Component: EventsPage }
        },
      },
      {
        path: 'wiki/:slug?',
        handle: {
          title: 'Wiki',
          subtitle: 'platform documentation',
        },
        lazy: async () => {
          const { WikiPage } = await import('../domains/collaboration/sections/wiki/WikiPage')

          return { Component: WikiPage }
        },
      },
      {
        path: 'cluster',
        handle: {
          title: 'Cluster',
          subtitle: 'nodes · agents · join workers',
        },
        lazy: async () => {
          const { ClusterPage } = await import('../domains/operations/sections/cluster/ClusterPage')

          return { Component: ClusterPage }
        },
      },
      {
        path: 'about',
        handle: {
          title: 'About',
          subtitle: 'PlaceContext — a full-scale data platform',
        },
        lazy: async () => {
          const { AboutPage } = await import('../domains/system/sections/about/AboutPage')

          return { Component: AboutPage }
        },
      },
      {
        path: 'onboarding',
        lazy: async () => {
          const { OnboardingPage } =
            await import('../domains/identity/sections/onboarding/OnboardingPage')

          return { Component: OnboardingPage }
        },
      },
      {
        path: 'settings',
        element: <SettingsLayout />,
        children: [
          {
            path: 'access',
            handle: {
              title: 'Access',
              subtitle: 'members, roles and permission overrides',
            },
            lazy: async () => {
              const { AccessSettingsPage } =
                await import('../domains/settings/sections/access/AccessSettingsPage')
              return { Component: AccessSettingsPage }
            },
          },
          {
            path: 'api-tokens',
            handle: {
              title: 'API tokens',
              subtitle: 'personal tokens for project data and search',
            },
            lazy: async () => {
              const { ApiTokensPage } =
                await import('../domains/settings/sections/api-tokens/ApiTokensPage')
              return { Component: ApiTokensPage }
            },
          },
          {
            path: 'artifacts',
            handle: { title: 'Settings', subtitle: 'Artifact filters' },
            lazy: async () => {
              const { ArtifactFiltersPage } =
                await import('../domains/settings/sections/artifacts/ArtifactFiltersPage')
              return { Component: ArtifactFiltersPage }
            },
          },
          {
            path: 'branding',
            handle: {
              title: 'Branding',
              subtitle: 'whitelabel the portal for this workspace',
            },
            lazy: async () => {
              const { BrandingPage } =
                await import('../domains/settings/sections/branding/BrandingPage')
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
              const { BackupPage } = await import('../domains/settings/sections/backup/BackupPage')

              return { Component: BackupPage }
            },
          },
          {
            path: 'communications',
            handle: { title: 'Settings', subtitle: 'Communications' },
            lazy: async () => {
              const { CommunicationsPage } =
                await import('../domains/settings/sections/communications/CommunicationsPage')
              return { Component: CommunicationsPage }
            },
          },
          {
            path: 'connections',
            handle: {
              title: 'Connections',
              subtitle: 'external databases and search indices per project',
            },
            lazy: async () => {
              const { ConnectionsPage } =
                await import('../domains/settings/sections/connections/ConnectionsPage')
              return { Component: ConnectionsPage }
            },
          },
          {
            path: 'locality',
            handle: {
              title: 'Locality',
              subtitle: 'workspace timezone — schedules and displayed times obey it',
            },
            lazy: async () => {
              const { LocalityPage } =
                await import('../domains/settings/sections/locality/LocalityPage')
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
          {
            path: 'mcp',
            handle: { title: 'Settings', subtitle: 'MCP servers' },
            lazy: async () => {
              const { McpSettingsPage } =
                await import('../domains/settings/sections/mcp/McpSettingsPage')
              return { Component: McpSettingsPage }
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
          const { LoginPage } = await import('../domains/identity/sections/login/LoginPage')

          return { Component: LoginPage }
        },
      },
      {
        path: 'setup',
        lazy: async () => {
          const { SetupPage } = await import('../domains/identity/sections/setup/SetupPage')

          return { Component: SetupPage }
        },
      },
    ],
  },
]

export const appRouter = createBrowserRouter(appRoutes, { basename: '/app' })
