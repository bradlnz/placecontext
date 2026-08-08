import { queryOptions } from '@tanstack/react-query'
import { fetchSchedules } from './schedules-api'
export function schedulesQueryOptions(projectId: string) {
  return queryOptions({
    queryKey: ['automation', 'schedules', projectId] as const,
    queryFn: async ({ signal }) => fetchSchedules(projectId, signal),
  })
}
