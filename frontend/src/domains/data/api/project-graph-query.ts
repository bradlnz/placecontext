import { queryOptions } from '@tanstack/react-query'

import { fetchProjectGraph } from './project-graph-api'

export function projectGraphQueryOptions(projectId: string) {
  return queryOptions({
    queryKey: ['data', 'project-graph', projectId] as const,
    queryFn: async ({ signal }) => fetchProjectGraph(projectId, signal),
  })
}
