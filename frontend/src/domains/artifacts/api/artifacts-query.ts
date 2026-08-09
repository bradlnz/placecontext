import { queryOptions } from '@tanstack/react-query'

import { fetchArtifactsPage } from './artifacts-api'

export const artifactsQueryKeys = {
  page: (projectId: string, search: string) => ['artifacts-page', projectId, search] as const,
}

export const artifactsPageQueryOptions = (projectId: string, search: string) =>
  queryOptions({
    queryKey: artifactsQueryKeys.page(projectId, search),
    queryFn: ({ signal }) => fetchArtifactsPage(projectId, search, signal),
  })
