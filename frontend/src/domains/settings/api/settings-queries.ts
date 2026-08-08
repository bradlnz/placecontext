import { queryOptions } from '@tanstack/react-query'

import { fetchArtifactFilters, fetchBranding, fetchLocality, fetchMenu } from './settings-api'

export const brandingQueryOptions = queryOptions({
  queryKey: ['settings', 'branding'],
  queryFn: async ({ signal }) => fetchBranding(signal),
})

export const localityQueryOptions = queryOptions({
  queryKey: ['settings', 'locality'],
  queryFn: async ({ signal }) => fetchLocality(signal),
})

export const menuQueryOptions = queryOptions({
  queryKey: ['settings', 'menu'],
  queryFn: async ({ signal }) => fetchMenu(signal),
})

export const artifactFiltersQueryOptions = queryOptions({
  queryKey: ['settings', 'artifacts'],
  queryFn: async ({ signal }) => fetchArtifactFilters(signal),
})
