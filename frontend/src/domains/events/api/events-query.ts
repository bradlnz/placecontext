import { queryOptions } from '@tanstack/react-query'
import { fetchEvents } from './events-api'

export const eventsQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: ['events', projectId] as const,
    queryFn: async ({ signal }) => fetchEvents(projectId, signal),
  })
