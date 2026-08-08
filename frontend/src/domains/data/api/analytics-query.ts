import { queryOptions } from '@tanstack/react-query'
import { fetchAnalytics } from './analytics-api'
export function analyticsQueryOptions(projectId: string) {
  return queryOptions({
    queryKey: ['data', 'analytics', projectId] as const,
    queryFn: async ({ signal }) => fetchAnalytics(projectId, signal),
    refetchInterval: (query) =>
      query.state.data?.sweepPending === true || (query.state.data?.pendingTables.length ?? 0) > 0
        ? 5_000
        : false,
  })
}
