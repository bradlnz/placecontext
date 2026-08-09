export type HostRouteMigrationStatus = 'migrated' | 'planned'

export interface HostRouteDefinition {
  domain: string
  section: string
  hostPath: string
  reactPath: string
  status: HostRouteMigrationStatus
}

/**
 * The complete routed surface declared by PlaceContext.Host/Components/Pages.
 *
 * Keep paths stable during the side-by-side phase: `/overview` in the Host is
 * `/app/overview` in React. Once React replaces Blazor, the basename can be
 * removed without changing the section route definitions.
 */
export const hostRouteCatalog = [
  {
    domain: 'system',
    section: 'dashboard',
    hostPath: '/',
    reactPath: '/',
    status: 'migrated',
  },
  {
    domain: 'workspace',
    section: 'overview',
    hostPath: '/overview',
    reactPath: '/overview',
    status: 'migrated',
  },
  {
    domain: 'workspace',
    section: 'inspector',
    hostPath: '/inspector',
    reactPath: '/inspector',
    status: 'migrated',
  },
  {
    domain: 'projects',
    section: 'project',
    hostPath: '/project/{Id:guid}',
    reactPath: '/project/:projectId',
    status: 'migrated',
  },
  {
    domain: 'crm',
    section: 'workspace',
    hostPath: '/project/{ProjectId:guid}/crm',
    reactPath: '/project/:projectId/crm',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'jobs',
    hostPath: '/project/{ProjectId:guid}/jobs',
    reactPath: '/project/:projectId/jobs',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'job-editor',
    hostPath: '/project/{ProjectId:guid}/jobs/{JobId:guid}',
    reactPath: '/project/:projectId/jobs/:jobId',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'tests',
    hostPath: '/project/{ProjectId:guid}/tests',
    reactPath: '/project/:projectId/tests',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'test-editor',
    hostPath: '/project/{ProjectId:guid}/tests/{TestId:guid}',
    reactPath: '/project/:projectId/tests/:testId',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'chains',
    hostPath: '/project/{ProjectId:guid}/chains',
    reactPath: '/project/:projectId/chains',
    status: 'migrated',
  },
  {
    domain: 'automation',
    section: 'schedules',
    hostPath: '/project/{ProjectId:guid}/schedules',
    reactPath: '/project/:projectId/schedules',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'project-data',
    hostPath: '/project/{ProjectId:guid}/data',
    reactPath: '/project/:projectId/data',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'entities',
    hostPath: '/project/{ProjectId:guid}/entities',
    reactPath: '/project/:projectId/entities',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'entity',
    hostPath: '/project/{ProjectId:guid}/entity/{EntityName}',
    reactPath: '/project/:projectId/entity/:entityName',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'graph',
    hostPath: '/project/{ProjectId:guid}/data-graph',
    reactPath: '/project/:projectId/data-graph',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'map',
    hostPath: '/project/{ProjectId:guid}/datamap',
    reactPath: '/project/:projectId/datamap',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'search',
    hostPath: '/project/{ProjectId:guid}/data-search',
    reactPath: '/project/:projectId/data-search',
    status: 'migrated',
  },
  {
    domain: 'data',
    section: 'analytics',
    hostPath: '/project/{ProjectId:guid}/analytics',
    reactPath: '/project/:projectId/analytics',
    status: 'migrated',
  },
  {
    domain: 'security',
    section: 'vault',
    hostPath: '/project/{ProjectId:guid}/secrets',
    reactPath: '/project/:projectId/secrets',
    status: 'migrated',
  },
  {
    domain: 'events',
    section: 'project-events',
    hostPath: '/project/{ProjectId:guid}/events',
    reactPath: '/project/:projectId/events',
    status: 'migrated',
  },
  {
    domain: 'collaboration',
    section: 'chat',
    hostPath: '/chat',
    reactPath: '/chat',
    status: 'migrated',
  },
  {
    domain: 'collaboration',
    section: 'wiki',
    hostPath: '/wiki',
    reactPath: '/wiki',
    status: 'migrated',
  },
  {
    domain: 'collaboration',
    section: 'wiki-article',
    hostPath: '/wiki/{Slug}',
    reactPath: '/wiki/:slug',
    status: 'migrated',
  },
  {
    domain: 'artifacts',
    section: 'library',
    hostPath: '/artifacts',
    reactPath: '/artifacts',
    status: 'migrated',
  },
  {
    domain: 'operations',
    section: 'observability',
    hostPath: '/observability',
    reactPath: '/observability',
    status: 'migrated',
  },
  {
    domain: 'operations',
    section: 'cluster',
    hostPath: '/cluster',
    reactPath: '/cluster',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'branding',
    hostPath: '/settings/branding',
    reactPath: '/settings/branding',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'access',
    hostPath: '/settings/access',
    reactPath: '/settings/access',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'api-tokens',
    hostPath: '/settings/api-tokens',
    reactPath: '/settings/api-tokens',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'artifacts',
    hostPath: '/settings/artifacts',
    reactPath: '/settings/artifacts',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'backup',
    hostPath: '/settings/backup',
    reactPath: '/settings/backup',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'communications',
    hostPath: '/settings/communications',
    reactPath: '/settings/communications',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'connections',
    hostPath: '/settings/connections',
    reactPath: '/settings/connections',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'locality',
    hostPath: '/settings/locality',
    reactPath: '/settings/locality',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'mcp',
    hostPath: '/settings/mcp',
    reactPath: '/settings/mcp',
    status: 'migrated',
  },
  {
    domain: 'settings',
    section: 'menu',
    hostPath: '/settings/menu',
    reactPath: '/settings/menu',
    status: 'migrated',
  },
  {
    domain: 'system',
    section: 'about',
    hostPath: '/about',
    reactPath: '/about',
    status: 'migrated',
  },
  {
    domain: 'identity',
    section: 'login',
    hostPath: '/login',
    reactPath: '/login',
    status: 'migrated',
  },
  {
    domain: 'identity',
    section: 'onboarding',
    hostPath: '/onboarding',
    reactPath: '/onboarding',
    status: 'migrated',
  },
  {
    domain: 'identity',
    section: 'setup',
    hostPath: '/setup',
    reactPath: '/setup',
    status: 'migrated',
  },
] as const satisfies readonly HostRouteDefinition[]
