import { queryOptions } from '@tanstack/react-query'

import { fetchProjectPage } from './project-page-api'

export function projectPageQueryOptions(projectId: string) {
  return queryOptions({
    queryKey: ['projects', 'page', projectId] as const,
    queryFn: async ({ signal }) => fetchProjectPage(projectId, signal),
  })
}
