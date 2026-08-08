import { queryOptions } from '@tanstack/react-query'

import { fetchCluster } from './cluster-api'

export const clusterQueryOptions = queryOptions({
  queryKey: ['operations', 'cluster'] as const,
  queryFn: async ({ signal }) => fetchCluster(signal),
})
