import { queryOptions } from '@tanstack/react-query'

import { fetchConnections } from './connections-api'

export const connectionsQueryOptions = queryOptions({
  queryKey: ['settings', 'connections'],
  queryFn: ({ signal }) => fetchConnections(signal),
  staleTime: 30_000,
})
