import { queryOptions } from '@tanstack/react-query'

import { fetchIdentityContext } from './identity-api'

export const identityContextQueryKey = ['identity', 'context'] as const

export const identityContextQuery = queryOptions({
  queryKey: identityContextQueryKey,
  queryFn: async ({ signal }) => fetchIdentityContext(signal),
  staleTime: 0,
  gcTime: 0,
})
