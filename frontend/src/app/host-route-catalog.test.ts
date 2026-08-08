import { describe, expect, it } from 'vitest'

import { hostRouteCatalog } from './host-route-catalog'

const PLACE_CONTEXT_HOST_ROUTES = [
  '/',
  '/about',
  '/artifacts',
  '/chat',
  '/cluster',
  '/inspector',
  '/login',
  '/observability',
  '/onboarding',
  '/overview',
  '/project/{Id:guid}',
  '/project/{ProjectId:guid}/analytics',
  '/project/{ProjectId:guid}/chains',
  '/project/{ProjectId:guid}/crm',
  '/project/{ProjectId:guid}/data',
  '/project/{ProjectId:guid}/data-graph',
  '/project/{ProjectId:guid}/data-search',
  '/project/{ProjectId:guid}/datamap',
  '/project/{ProjectId:guid}/entities',
  '/project/{ProjectId:guid}/entity/{EntityName}',
  '/project/{ProjectId:guid}/events',
  '/project/{ProjectId:guid}/jobs',
  '/project/{ProjectId:guid}/jobs/{JobId:guid}',
  '/project/{ProjectId:guid}/schedules',
  '/project/{ProjectId:guid}/secrets',
  '/project/{ProjectId:guid}/tests',
  '/project/{ProjectId:guid}/tests/{TestId:guid}',
  '/settings/access',
  '/settings/api-tokens',
  '/settings/artifacts',
  '/settings/backup',
  '/settings/branding',
  '/settings/communications',
  '/settings/connections',
  '/settings/locality',
  '/settings/mcp',
  '/settings/menu',
  '/setup',
  '/wiki',
  '/wiki/{Slug}',
] as const

describe('hostRouteCatalog', () => {
  it('accounts for every routed PlaceContext.Host page exactly once', () => {
    const catalogPaths = hostRouteCatalog.map(({ hostPath }) => hostPath).toSorted()

    expect(catalogPaths).toEqual(PLACE_CONTEXT_HOST_ROUTES.toSorted())
    expect(new Set(catalogPaths)).toHaveLength(catalogPaths.length)
  })

  it('keeps React route definitions unique and domain-owned', () => {
    const reactPaths = hostRouteCatalog.map(({ reactPath }) => reactPath)

    expect(new Set(reactPaths)).toHaveLength(reactPaths.length)
    expect(hostRouteCatalog.every(({ domain, section }) => domain.length > 0 && section.length > 0)).toBe(true)
  })
})
