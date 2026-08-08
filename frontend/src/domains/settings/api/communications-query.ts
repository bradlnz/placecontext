import { queryOptions } from '@tanstack/react-query'

import { fetchCommunications } from './communications-api'

export const communicationsQueryOptions = queryOptions({
  queryKey: ['settings', 'communications'],
  queryFn: async ({ signal }) => fetchCommunications(signal),
})
