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
  { domain: 'system', section: 'dashboard', hostPath: '/', reactPath: '/', status: 'migrated' },
  { domain: 'workspace', section: 'overview', hostPath: '/overview', reactPath: '/overview', status: 'migrated' },
  { domain: 'workspace', section: 'inspector', hostPath: '/inspector', reactPath: '/inspector', status: 'planned' },
  { domain: 'projects', section: 'project', hostPath: '/project/{Id:guid}', reactPath: '/project/:projectId', status: 'planned' },
  { domain: 'crm', section: 'workspace', hostPath: '/project/{ProjectId:guid}/crm', reactPath: '/project/:projectId/crm', status: 'planned' },
  { domain: 'automation', section: 'jobs', hostPath: '/project/{ProjectId:guid}/jobs', reactPath: '/project/:projectId/jobs', status: 'planned' },
  { domain: 'automation', section: 'job-editor', hostPath: '/project/{ProjectId:guid}/jobs/{JobId:guid}', reactPath: '/project/:projectId/jobs/:jobId', status: 'planned' },
  { domain: 'automation', section: 'tests', hostPath: '/project/{ProjectId:guid}/tests', reactPath: '/project/:projectId/tests', status: 'planned' },
  { domain: 'automation', section: 'test-editor', hostPath: '/project/{ProjectId:guid}/tests/{TestId:guid}', reactPath: '/project/:projectId/tests/:testId', status: 'planned' },
  { domain: 'automation', section: 'chains', hostPath: '/project/{ProjectId:guid}/chains', reactPath: '/project/:projectId/chains', status: 'planned' },
  { domain: 'automation', section: 'schedules', hostPath: '/project/{ProjectId:guid}/schedules', reactPath: '/project/:projectId/schedules', status: 'planned' },
  { domain: 'data', section: 'project-data', hostPath: '/project/{ProjectId:guid}/data', reactPath: '/project/:projectId/data', status: 'planned' },
  { domain: 'data', section: 'entities', hostPath: '/project/{ProjectId:guid}/entities', reactPath: '/project/:projectId/entities', status: 'planned' },
  { domain: 'data', section: 'entity', hostPath: '/project/{ProjectId:guid}/entity/{EntityName}', reactPath: '/project/:projectId/entity/:entityName', status: 'planned' },
  { domain: 'data', section: 'graph', hostPath: '/project/{ProjectId:guid}/data-graph', reactPath: '/project/:projectId/data-graph', status: 'planned' },
  { domain: 'data', section: 'map', hostPath: '/project/{ProjectId:guid}/datamap', reactPath: '/project/:projectId/datamap', status: 'planned' },
  { domain: 'data', section: 'search', hostPath: '/project/{ProjectId:guid}/data-search', reactPath: '/project/:projectId/data-search', status: 'planned' },
  { domain: 'data', section: 'analytics', hostPath: '/project/{ProjectId:guid}/analytics', reactPath: '/project/:projectId/analytics', status: 'planned' },
  { domain: 'security', section: 'vault', hostPath: '/project/{ProjectId:guid}/secrets', reactPath: '/project/:projectId/secrets', status: 'planned' },
  { domain: 'events', section: 'project-events', hostPath: '/project/{ProjectId:guid}/events', reactPath: '/project/:projectId/events', status: 'planned' },
  { domain: 'collaboration', section: 'chat', hostPath: '/chat', reactPath: '/chat', status: 'planned' },
  { domain: 'collaboration', section: 'wiki', hostPath: '/wiki', reactPath: '/wiki', status: 'planned' },
  { domain: 'collaboration', section: 'wiki-article', hostPath: '/wiki/{Slug}', reactPath: '/wiki/:slug', status: 'planned' },
  { domain: 'artifacts', section: 'library', hostPath: '/artifacts', reactPath: '/artifacts', status: 'planned' },
  { domain: 'operations', section: 'observability', hostPath: '/observability', reactPath: '/observability', status: 'planned' },
  { domain: 'operations', section: 'cluster', hostPath: '/cluster', reactPath: '/cluster', status: 'planned' },
  { domain: 'settings', section: 'branding', hostPath: '/settings/branding', reactPath: '/settings/branding', status: 'migrated' },
  { domain: 'settings', section: 'access', hostPath: '/settings/access', reactPath: '/settings/access', status: 'planned' },
  { domain: 'settings', section: 'api-tokens', hostPath: '/settings/api-tokens', reactPath: '/settings/api-tokens', status: 'migrated' },
  { domain: 'settings', section: 'artifacts', hostPath: '/settings/artifacts', reactPath: '/settings/artifacts', status: 'migrated' },
  { domain: 'settings', section: 'backup', hostPath: '/settings/backup', reactPath: '/settings/backup', status: 'migrated' },
  { domain: 'settings', section: 'communications', hostPath: '/settings/communications', reactPath: '/settings/communications', status: 'migrated' },
  { domain: 'settings', section: 'connections', hostPath: '/settings/connections', reactPath: '/settings/connections', status: 'migrated' },
  { domain: 'settings', section: 'locality', hostPath: '/settings/locality', reactPath: '/settings/locality', status: 'migrated' },
  { domain: 'settings', section: 'mcp', hostPath: '/settings/mcp', reactPath: '/settings/mcp', status: 'planned' },
  { domain: 'settings', section: 'menu', hostPath: '/settings/menu', reactPath: '/settings/menu', status: 'migrated' },
  { domain: 'system', section: 'about', hostPath: '/about', reactPath: '/about', status: 'migrated' },
  { domain: 'identity', section: 'login', hostPath: '/login', reactPath: '/login', status: 'migrated' },
  { domain: 'identity', section: 'onboarding', hostPath: '/onboarding', reactPath: '/onboarding', status: 'migrated' },
  { domain: 'identity', section: 'setup', hostPath: '/setup', reactPath: '/setup', status: 'migrated' },
] as const satisfies readonly HostRouteDefinition[]
